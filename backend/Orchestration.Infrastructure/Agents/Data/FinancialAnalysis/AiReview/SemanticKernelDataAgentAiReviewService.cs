using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Chat;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;
using Orchestration.Infrastructure.Agents.Planner.Reasoning;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.AiReview;

public sealed class SemanticKernelDataAgentAiReviewService : IDataAgentAiReviewService
{
    private const int MaxItemsPerCollection = 25;

    private const string SystemPrompt = """
You are reviewing structured financial analysis results.

Rules:
- Use only the provided financial analysis evidence.
- Do not recompute financial ratios.
- Do not create new metrics.
- Do not modify risk signals.
- Do not provide investment advice.
- Do not claim accounting correctness.
- Do not make legal conclusions.
- Do not mention data that is not present in the input.
- Return JSON only.

Return valid JSON only.
Do not include markdown.
Do not include code fences.
Do not include explanatory text outside JSON.

Return this JSON shape:
{
  "summary": "string",
  "keyFindings": [
    {
      "title": "string",
      "description": "string",
      "severity": "Low|Medium|High|Info|Warning",
      "relatedMetrics": ["string"]
    }
  ],
  "riskInterpretation": "string",
  "dataQualityNotes": [
    {
      "message": "string",
      "severity": "Info|Warning|Low|Medium|High",
      "relatedFields": ["string"]
    }
  ],
  "limitations": ["string"]
}
""";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly LlmOptions _options;
    private readonly DeterministicDataAgentAiReviewService _fallback;
    private readonly SemanticKernelDataAgentAiReviewResponseParser _parser;
    private readonly ILogger<SemanticKernelDataAgentAiReviewService> _logger;
    private readonly Kernel? _kernel;
    private readonly IChatCompletionService _chatCompletionService;

    public SemanticKernelDataAgentAiReviewService(
        IOptions<LlmOptions> options,
        DeterministicDataAgentAiReviewService fallback,
        SemanticKernelDataAgentAiReviewResponseParser parser,
        ILogger<SemanticKernelDataAgentAiReviewService> logger)
    {
        _options = options.Value;
        _fallback = fallback;
        _parser = parser;
        _logger = logger;

        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.AddOpenAIChatCompletion(
            modelId: _options.Model,
            apiKey: _options.ApiKey,
            serviceId: _options.ServiceId
        );

        _kernel = kernelBuilder.Build();
        _chatCompletionService = _kernel.GetRequiredService<IChatCompletionService>(
            _options.ServiceId
        );
    }

    internal SemanticKernelDataAgentAiReviewService(
        LlmOptions options,
        DeterministicDataAgentAiReviewService fallback,
        SemanticKernelDataAgentAiReviewResponseParser parser,
        IChatCompletionService chatCompletionService,
        ILogger<SemanticKernelDataAgentAiReviewService>? logger = null)
    {
        _options = options;
        _fallback = fallback;
        _parser = parser;
        _chatCompletionService = chatCompletionService;
        _logger = logger ?? NullLogger<SemanticKernelDataAgentAiReviewService>.Instance;
    }

    public async Task<FinancialAnalysisAiReviewResult> ReviewAsync(
        FinancialAnalysisAiReviewInput input,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return await CreateFallbackResultAsync(
                input,
                "llm_disabled",
                cancellationToken
            );
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
            return await CreateFallbackResultAsync(
                input,
                "timeout",
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "DataAgent AI review provider failed; deterministic fallback will be used."
            );

            return await CreateFallbackResultAsync(
                input,
                "provider_error",
                cancellationToken
            );
        }
    }

    private async Task<FinancialAnalysisAiReviewResult> CreateLlmResultOrFallbackAsync(
        FinancialAnalysisAiReviewInput input,
        SemanticKernelDataAgentAiReviewParsedResponse parsed,
        CancellationToken cancellationToken)
    {
        var allowedMetrics = BuildAllowedRelatedMetrics(input);
        var unknownRelatedMetricsRemoved = false;

        var sanitizedFindings = parsed.KeyFindings
            .Select(finding =>
            {
                var relatedMetrics = finding.RelatedMetrics
                    .Where(metric => allowedMetrics.Contains(metric))
                    .ToArray();

                if (relatedMetrics.Length != finding.RelatedMetrics.Count)
                {
                    unknownRelatedMetricsRemoved = true;
                }

                return finding with
                {
                    RelatedMetrics = relatedMetrics
                };
            })
            .ToArray();

        var limitations = parsed.Limitations
            .Concat(
                unknownRelatedMetricsRemoved
                    ? ["Unknown related metrics returned by the AI review were removed."]
                    : []
            )
            .Where(limitation => !string.IsNullOrWhiteSpace(limitation))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new FinancialAnalysisAiReviewResult(
            Summary: parsed.Summary,
            KeyFindings: sanitizedFindings,
            RiskInterpretation: parsed.RiskInterpretation,
            DataQualityNotes: parsed.DataQualityNotes,
            Limitations: limitations,
            UsedLlm: true,
            UsedFallback: false,
            Provider: ProviderName,
            Model: _options.Model,
            FailureReason: null
        );

        if (ContainsUnsafeLanguage(result))
        {
            return await CreateFallbackResultAsync(
                input,
                "unsafe_content",
                cancellationToken
            );
        }

        return result;
    }

    private async Task<FinancialAnalysisAiReviewResult> CreateFallbackResultAsync(
        FinancialAnalysisAiReviewInput input,
        string failureReason,
        CancellationToken cancellationToken)
    {
        var fallback = await _fallback.ReviewAsync(
            input,
            cancellationToken
        );

        return ApplyFallbackMetadata(
            fallback,
            _options,
            failureReason
        );
    }

    internal static FinancialAnalysisAiReviewResult ApplyFallbackMetadata(
        FinancialAnalysisAiReviewResult fallback,
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

    private string ProviderName =>
        NormalizeProvider(_options.Provider);

    private static string NormalizeProvider(string provider)
    {
        return string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase)
            ? "OpenAI"
            : provider;
    }

    private static HashSet<string> BuildAllowedRelatedMetrics(
        FinancialAnalysisAiReviewInput input)
    {
        return input.Ratios
            .Select(ratio => ratio.Name)
            .Concat(input.PeriodComparisons.Select(comparison => comparison.MetricName))
            .Concat(input.RiskSignals.SelectMany(signal => signal.Evidence.Select(evidence => evidence.MetricName)))
            .Concat(input.RiskEvidence.Select(evidence => evidence.MetricName))
            .Where(metric => !string.IsNullOrWhiteSpace(metric))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool ContainsUnsafeLanguage(FinancialAnalysisAiReviewResult result)
    {
        return EnumerateOutputStrings(result).Any(ContainsUnsafeLanguage);
    }

    private static bool ContainsUnsafeLanguage(string value)
    {
        var text = value.ToLowerInvariant();

        return text.Contains("investment advice", StringComparison.Ordinal) ||
            text.Contains("illegal", StringComparison.Ordinal) ||
            text.Contains("guaranteed", StringComparison.Ordinal) ||
            text.Contains("accounting correctness confirmed", StringComparison.Ordinal) ||
            ContainsWord(text, "buy") ||
            ContainsWord(text, "sell");
    }

    private static bool ContainsWord(string text, string word)
    {
        var index = text.IndexOf(word, StringComparison.Ordinal);

        while (index >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var afterIndex = index + word.Length;
            var after = afterIndex >= text.Length || !char.IsLetterOrDigit(text[afterIndex]);

            if (before && after)
            {
                return true;
            }

            index = text.IndexOf(word, index + word.Length, StringComparison.Ordinal);
        }

        return false;
    }

    private static IEnumerable<string> EnumerateOutputStrings(FinancialAnalysisAiReviewResult result)
    {
        yield return result.Summary;
        yield return result.RiskInterpretation;

        foreach (var limitation in result.Limitations)
        {
            yield return limitation;
        }

        foreach (var finding in result.KeyFindings)
        {
            yield return finding.Title;
            yield return finding.Description;
            yield return finding.Severity;

            foreach (var metric in finding.RelatedMetrics)
            {
                yield return metric;
            }
        }

        foreach (var note in result.DataQualityNotes)
        {
            yield return note.Message;
            yield return note.Severity;

            foreach (var field in note.RelatedFields)
            {
                yield return field;
            }
        }
    }

    private static string BuildUserPrompt(FinancialAnalysisAiReviewInput input)
    {
        var payload = new
        {
            input.SessionId,
            input.DocumentId,
            input.Company,
            input.MetricsInputSource,
            metricsProvenance = input.MetricsProvenance is null
                ? null
                : new
                {
                    input.MetricsProvenance.IngestionMethod,
                    input.MetricsProvenance.OriginalFileName,
                    input.MetricsProvenance.FileSizeBytes,
                    input.MetricsProvenance.MetricCount,
                    input.MetricsProvenance.WarningCount
                },
            ratios = input.Ratios.Take(MaxItemsPerCollection),
            periodComparisons = input.PeriodComparisons.Take(MaxItemsPerCollection),
            riskSignals = input.RiskSignals.Take(MaxItemsPerCollection),
            riskEvidence = input.RiskEvidence.Take(MaxItemsPerCollection),
            warnings = input.Warnings.Take(MaxItemsPerCollection),
            limitations = input.Limitations.Take(MaxItemsPerCollection)
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
