using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Chat;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Legal.AiReview;
using Orchestration.Infrastructure.Agents.Planner.Reasoning;

namespace Orchestration.Infrastructure.Agents.Legal.AiReview;

public sealed class SemanticKernelLegalAnalysisReviewService : ILegalAnalysisReviewService
{
    private const string SystemPrompt = """
You are reviewing a financial analysis against provided CNV/Infoleg evidence.

Rules:
- Review only the provided financial analysis and legal evidence.
- Use only the provided CNV/Infoleg evidence references.
- Do not invent regulations.
- Do not invent citations.
- Do not provide legal advice.
- Do not declare legal violations.
- Do not say the company violated, breached, committed fraud, or is guilty.
- Use cautious language such as "possible regulatory review area".
- Every possible regulatory review area must include at least one evidence citation from the provided evidence.
- If evidence is insufficient, state that as a limitation.
- Return JSON only.

Return valid JSON only.
Do not include markdown.
Do not include code fences.
Do not include explanatory text outside JSON.

Return this JSON shape:
{
  "reviewSummary": "string",
  "possibleRegulatoryReviewAreas": [
    {
      "title": "string",
      "description": "string",
      "severity": "Low|Medium|High|Info|Warning",
      "relatedFinancialSignals": ["string"],
      "evidenceCitations": ["string"]
    }
  ],
  "evidenceReferences": [
    {
      "source": "string",
      "title": "string",
      "url": "string|null",
      "citation": "string|null",
      "snippet": "string|null",
      "regulationArea": "string|null",
      "score": 0.0
    }
  ],
  "warnings": ["string"],
  "limitations": ["string"]
}
""";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly LlmOptions _llmOptions;
    private readonly LegalAgentOptions _legalAgentOptions;
    private readonly DeterministicLegalAnalysisReviewService _fallback;
    private readonly SemanticKernelLegalAnalysisReviewResponseParser _parser;
    private readonly ILogger<SemanticKernelLegalAnalysisReviewService> _logger;
    private readonly Kernel? _kernel;
    private readonly IChatCompletionService? _chatCompletionService;

    public SemanticKernelLegalAnalysisReviewService(
        IOptions<LlmOptions> llmOptions,
        IOptions<LegalAgentOptions> legalAgentOptions,
        DeterministicLegalAnalysisReviewService fallback,
        SemanticKernelLegalAnalysisReviewResponseParser parser,
        ILogger<SemanticKernelLegalAnalysisReviewService> logger)
    {
        _llmOptions = llmOptions.Value;
        _legalAgentOptions = legalAgentOptions.Value;
        _fallback = fallback;
        _parser = parser;
        _logger = logger;

        if (_llmOptions.Enabled && _legalAgentOptions.AiReviewEnabled && IsConfigValid(_llmOptions))
        {
            try
            {
                var kernelBuilder = Kernel.CreateBuilder();
                kernelBuilder.AddOpenAIChatCompletion(
                    modelId: _llmOptions.Model,
                    apiKey: _llmOptions.ApiKey,
                    serviceId: _llmOptions.ServiceId
                );

                _kernel = kernelBuilder.Build();
                _chatCompletionService = _kernel.GetRequiredService<IChatCompletionService>(
                    _llmOptions.ServiceId
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Semantic Kernel ChatCompletionService.");
            }
        }
    }

    internal SemanticKernelLegalAnalysisReviewService(
        LlmOptions llmOptions,
        LegalAgentOptions legalAgentOptions,
        DeterministicLegalAnalysisReviewService fallback,
        SemanticKernelLegalAnalysisReviewResponseParser parser,
        IChatCompletionService chatCompletionService,
        ILogger<SemanticKernelLegalAnalysisReviewService>? logger = null)
    {
        _llmOptions = llmOptions;
        _legalAgentOptions = legalAgentOptions;
        _fallback = fallback;
        _parser = parser;
        _chatCompletionService = chatCompletionService;
        _logger = logger ?? NullLogger<SemanticKernelLegalAnalysisReviewService>.Instance;
    }

    public async Task<LegalAnalysisReviewResult> ReviewAsync(
        LegalAnalysisReviewInput input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_llmOptions.Enabled)
        {
            return await CreateFallbackResultAsync(input, "llm_disabled", cancellationToken);
        }

        if (!_legalAgentOptions.AiReviewEnabled)
        {
            return await CreateFallbackResultAsync(input, "llm_disabled", cancellationToken);
        }

        if (!IsConfigValid(_llmOptions))
        {
            return await CreateFallbackResultAsync(input, "llm_misconfigured", cancellationToken);
        }

        if (_chatCompletionService == null)
        {
            return await CreateFallbackResultAsync(input, "llm_misconfigured", cancellationToken);
        }

        try
        {
            var history = new ChatHistory();
            history.AddSystemMessage(SystemPrompt);
            history.AddUserMessage(BuildUserPrompt(input));

            var executionSettings = new OpenAIPromptExecutionSettings
            {
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
            };

            var response = await _chatCompletionService.GetChatMessageContentAsync(
                history,
                executionSettings,
                kernel: _kernel,
                cancellationToken: cancellationToken
            );

            var parseResult = _parser.Parse(response.Content);

            if (!parseResult.Succeeded || parseResult.Response is null)
            {
                return await CreateFallbackResultAsync(
                    input,
                    parseResult.FailureReason ?? "schema_validation_failed",
                    cancellationToken
                );
            }

            return await CreateLlmResultOrFallbackAsync(
                input,
                parseResult.Response,
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return await CreateFallbackResultAsync(input, "timeout", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "LegalAgent AI review provider failed; deterministic fallback will be used."
            );

            return await CreateFallbackResultAsync(input, "provider_error", cancellationToken);
        }
    }

    private async Task<LegalAnalysisReviewResult> CreateLlmResultOrFallbackAsync(
        LegalAnalysisReviewInput input,
        SemanticKernelLegalAnalysisReviewParsedResponse parsed,
        CancellationToken cancellationToken)
    {
        var allowedCitations = (input.CnvEvidence ?? Array.Empty<LegalEvidenceReference>())
            .Where(e => !string.IsNullOrWhiteSpace(e.Citation))
            .Select(e => e.Citation!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unknownCitationsRemoved = false;
        var areasRemoved = false;

        var sanitizedAreas = new List<PossibleRegulatoryReviewArea>();
        foreach (var area in parsed.PossibleRegulatoryReviewAreas)
        {
            var validCitations = (area.EvidenceCitations ?? Array.Empty<string>())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Where(c => allowedCitations.Contains(c))
                .ToArray();

            if (validCitations.Length != (area.EvidenceCitations?.Count ?? 0))
            {
                unknownCitationsRemoved = true;
            }

            if (validCitations.Length == 0)
            {
                areasRemoved = true;
                continue;
            }

            sanitizedAreas.Add(area with { EvidenceCitations = validCitations });
        }

        var unknownReferencesRemoved = false;
        var sanitizedReferences = (parsed.EvidenceReferences ?? Array.Empty<LegalEvidenceReference>())
            .Where(e => !string.IsNullOrWhiteSpace(e.Citation) && allowedCitations.Contains(e.Citation.Trim()))
            .ToArray();

        if ((parsed.EvidenceReferences?.Count ?? 0) != sanitizedReferences.Length)
        {
            unknownReferencesRemoved = true;
        }

        var extraWarnings = new List<string>();
        var extraLimitations = new List<string>();

        if (unknownCitationsRemoved || unknownReferencesRemoved)
        {
            extraWarnings.Add("Unknown citations or references returned by the AI review were removed.");
        }

        if (areasRemoved)
        {
            extraWarnings.Add("Some possible regulatory review areas were removed because they had no valid evidence citations.");
        }

        if (sanitizedAreas.Count == 0)
        {
            extraLimitations.Add("LLM output did not include review areas supported by provided citations.");
        }

        var finalWarnings = (parsed.Warnings ?? Array.Empty<string>())
            .Concat(extraWarnings)
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var finalLimitations = (parsed.Limitations ?? Array.Empty<string>())
            .Concat(extraLimitations)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new LegalAnalysisReviewResult(
            ReviewSummary: parsed.ReviewSummary,
            PossibleRegulatoryReviewAreas: sanitizedAreas,
            EvidenceReferences: sanitizedReferences,
            Warnings: finalWarnings,
            Limitations: finalLimitations,
            UsedLlm: true,
            UsedFallback: false,
            Provider: NormalizeProvider(_llmOptions.Provider),
            Model: _llmOptions.Model,
            FailureReason: null
        );

        if (ContainsForbiddenLanguage(result))
        {
            return await CreateFallbackResultAsync(input, "forbidden_language", cancellationToken);
        }

        return result;
    }

    private async Task<LegalAnalysisReviewResult> CreateFallbackResultAsync(
        LegalAnalysisReviewInput input,
        string failureReason,
        CancellationToken cancellationToken)
    {
        var fallback = await _fallback.ReviewAsync(input, cancellationToken);
        return ApplyFallbackMetadata(fallback, _llmOptions, failureReason);
    }

    internal static LegalAnalysisReviewResult ApplyFallbackMetadata(
        LegalAnalysisReviewResult fallback,
        LlmOptions options,
        string failureReason)
    {
        return fallback with
        {
            UsedLlm = false,
            UsedFallback = true,
            Provider = NormalizeProvider(options.Provider),
            Model = options.Model,
            FailureReason = failureReason
        };
    }

    private static string NormalizeProvider(string provider)
    {
        return string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase)
            ? "OpenAI"
            : provider;
    }

    private static bool IsConfigValid(LlmOptions options)
    {
        return options != null &&
               string.Equals(options.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(options.Model) &&
               !string.IsNullOrWhiteSpace(options.ApiKey) &&
               !string.IsNullOrWhiteSpace(options.ServiceId);
    }

    private static bool ContainsForbiddenLanguage(LegalAnalysisReviewResult result)
    {
        return EnumerateOutputStrings(result).Any(ContainsForbiddenLanguage);
    }

    private static bool ContainsForbiddenLanguage(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.ToLowerInvariant();
        return LegalAnalysisAiReviewLanguageRules.ForbiddenLanguage.Any(forbidden =>
            text.Contains(forbidden, StringComparison.Ordinal));
    }

    private static IEnumerable<string> EnumerateOutputStrings(LegalAnalysisReviewResult result)
    {
        yield return result.ReviewSummary;

        foreach (var area in result.PossibleRegulatoryReviewAreas)
        {
            yield return area.Title;
            yield return area.Description;
            yield return area.Severity;

            foreach (var sig in area.RelatedFinancialSignals)
            {
                yield return sig;
            }

            foreach (var cite in area.EvidenceCitations)
            {
                yield return cite;
            }
        }

        foreach (var refItem in result.EvidenceReferences)
        {
            yield return refItem.Source;
            yield return refItem.Title;
            if (refItem.Url != null) yield return refItem.Url;
            if (refItem.Citation != null) yield return refItem.Citation;
            if (refItem.Snippet != null) yield return refItem.Snippet;
            if (refItem.RegulationArea != null) yield return refItem.RegulationArea;
        }

        foreach (var warning in result.Warnings)
        {
            yield return warning;
        }

        foreach (var limitation in result.Limitations)
        {
            yield return limitation;
        }
    }

    private static string BuildUserPrompt(LegalAnalysisReviewInput input)
    {
        var payload = new
        {
            input.SessionId,
            input.Company,
            input.DocumentId,
            input.MetricsInputSource,
            financialRiskSignals = (input.FinancialRiskSignals ?? Array.Empty<FinancialRiskSignal>())
                .Select(s => new
                {
                    s.Name,
                    s.Severity,
                    s.Period,
                    s.Summary,
                    evidence = (s.Evidence ?? Array.Empty<RiskEvidenceItem>())
                        .Select(e => new
                        {
                            e.MetricName,
                            e.Period,
                            e.Value,
                            e.Threshold,
                            e.Unit,
                            e.Interpretation
                        })
                }),
            financialRiskEvidence = (input.FinancialRiskEvidence ?? Array.Empty<RiskEvidenceItem>())
                .Select(e => new
                {
                    e.MetricName,
                    e.Period,
                    e.Value,
                    e.Threshold,
                    e.Unit,
                    e.Interpretation
                }),
            financialAiReview = input.FinancialAiReview == null
                ? null
                : new
                {
                    input.FinancialAiReview.Summary,
                    keyFindings = (input.FinancialAiReview.KeyFindings ?? Array.Empty<FinancialAnalysisAiKeyFinding>())
                        .Select(kf => new
                        {
                            kf.Title,
                            kf.Description,
                            kf.Severity,
                            kf.RelatedMetrics
                        }),
                    input.FinancialAiReview.RiskInterpretation
                },
            financialWarnings = input.FinancialWarnings ?? Array.Empty<string>(),
            financialLimitations = input.FinancialLimitations ?? Array.Empty<string>(),
            cnvEvidence = (input.CnvEvidence ?? Array.Empty<LegalEvidenceReference>())
                .Select(e => new
                {
                    e.Source,
                    e.Title,
                    e.Url,
                    e.Citation,
                    e.Snippet,
                    e.RegulationArea,
                    e.Score
                })
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
