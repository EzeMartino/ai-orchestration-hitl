using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Infrastructure.Agents.Planner.Reasoning;

namespace Orchestration.Infrastructure.Agents.Legal.AiReview;

public sealed class SemanticKernelLegalAnalysisReviewService : ILegalAnalysisReviewService
{
    private const string LegalAdviceDisclaimer = "No constituye asesoramiento legal";

    private const string SystemPrompt = """
You are reviewing a financial analysis against provided CNV/Infoleg evidence.

Rules:
- Review only the provided financial analysis and legal evidence.
- Use only the provided CNV/Infoleg evidence references.
- Do not invent regulations.
- Do not invent citations.
- Retrieval and citations do not establish compliance risk or a legal violation.
- Evaluate evidence relevance, applicability, and evidence quality using the provided evidenceAssessment.
- If evidenceAssessment is provided, use its severity for every possible regulatory review area.
- When applicability is NotEstablished, the maximum severity is Warning and human review is required.
- The output is not legal advice and must not assert illegality, a legal violation, fraud, or guilt.
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
    private static readonly IReadOnlyList<string> ConclusiveLanguagePatterns =
        LegalAnalysisAiReviewLanguageRules.ConclusiveLanguage
            .Where(phrase =>
                !string.Equals(phrase, "culpable", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(phrase, "guilty", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(phrase => phrase.Length)
            .Select(BuildConclusiveLanguagePattern)
            .Concat(new[]
            {
                BuildFraudFamilyPattern(),
                BuildConfirmedNonComplianceFamilyPattern(),
                BuildCulpabilityFamilyPattern("es", "culpable"),
                BuildCulpabilityFamilyPattern("is", "guilty")
            })
            .ToArray();
    private static readonly string LegalAdviceDisclaimerPattern =
        BuildConclusiveLanguagePattern(LegalAdviceDisclaimer);
    private static readonly string LegalAdviceDisclaimerComparisonKey =
        BuildLegalAdviceDisclaimerComparisonKey(LegalAdviceDisclaimer);

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
        var allowedEvidenceByCitation = (input.CnvEvidence ?? Array.Empty<LegalEvidenceReference>())
            .Where(e => !string.IsNullOrWhiteSpace(e.Citation))
            .GroupBy(e => e.Citation!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var allowedSignalsByName = (input.FinancialRiskSignals ?? Array.Empty<FinancialRiskSignal>())
            .Where(signal => !string.IsNullOrWhiteSpace(signal.Name))
            .GroupBy(signal => signal.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Name,
                StringComparer.OrdinalIgnoreCase);

        var unknownCitationsRemoved = false;
        var areasRemoved = false;

        var sanitizedAreas = new List<PossibleRegulatoryReviewArea>();
        foreach (var area in parsed.PossibleRegulatoryReviewAreas)
        {
            var validCitations = (area.EvidenceCitations ?? Array.Empty<string>())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Where(c => allowedEvidenceByCitation.ContainsKey(c))
                .Select(c => allowedEvidenceByCitation[c].Citation!)
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

            var validSignals = (area.RelatedFinancialSignals ?? Array.Empty<string>())
                .Where(signal => !string.IsNullOrWhiteSpace(signal))
                .Select(signal => signal.Trim())
                .Where(signal => allowedSignalsByName.ContainsKey(signal))
                .Select(signal => allowedSignalsByName[signal])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            sanitizedAreas.Add(area with
            {
                RelatedFinancialSignals = validSignals,
                EvidenceCitations = validCitations
            });
        }

        var unknownReferencesRemoved = false;
        var sanitizedReferences = (parsed.EvidenceReferences ?? Array.Empty<LegalEvidenceReference>())
            .Where(e => !string.IsNullOrWhiteSpace(e.Citation) &&
                        allowedEvidenceByCitation.ContainsKey(e.Citation.Trim()))
            .Select(e => allowedEvidenceByCitation[e.Citation!.Trim()])
            .DistinctBy(e => e.Citation, StringComparer.OrdinalIgnoreCase)
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

        if (input.EvidenceAssessment is not null)
        {
            result = NormalizeAssessedResult(result, input.EvidenceAssessment);
        }

        if (ContainsForbiddenLanguage(result, useLegacySubstringMatching: input.EvidenceAssessment is null))
        {
            return await CreateFallbackResultAsync(input, "forbidden_language", cancellationToken);
        }

        return result;
    }

    private static LegalAnalysisReviewResult NormalizeAssessedResult(
        LegalAnalysisReviewResult result,
        RegulatoryEvidenceAssessment assessment)
    {
        var areas = result.PossibleRegulatoryReviewAreas
            .Select(area => area with
            {
                Title = SanitizeConclusiveLanguage(area.Title, assessment),
                Description = SanitizeConclusiveLanguage(area.Description, assessment),
                Severity = NormalizeAssessmentSeverity(assessment)
            })
            .ToArray();

        var warnings = result.Warnings
            .Select(value => SanitizeConclusiveLanguage(value, assessment))
            .ToArray();
        var limitations = result.Limitations
            .Select(value => SanitizeConclusiveLanguage(value, assessment))
            .Where(value => !IsEquivalentLegalAdviceDisclaimer(value))
            .Append(LegalAdviceDisclaimer)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return result with
        {
            ReviewSummary = SanitizeConclusiveLanguage(result.ReviewSummary, assessment),
            PossibleRegulatoryReviewAreas = areas,
            Warnings = warnings,
            Limitations = limitations
        };
    }

    private static string SanitizeConclusiveLanguage(
        string value,
        RegulatoryEvidenceAssessment assessment)
    {
        var replacement = string.Equals(
            assessment.Applicability,
            "NotEstablished",
            StringComparison.OrdinalIgnoreCase)
            ? "evidencia que requiere revisión humana porque su aplicabilidad no está establecida"
            : "posible área de revisión basada en la evidencia y su aplicabilidad evaluada";
        var sanitized = value.Normalize(NormalizationForm.FormD);

        foreach (var pattern in ConclusiveLanguagePatterns)
        {
            sanitized = Regex.Replace(
                sanitized,
                pattern,
                match => IsProtectedConclusiveLanguageMatch(sanitized, match)
                    ? match.Value
                    : replacement,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return sanitized.Normalize(NormalizationForm.FormC);
    }

    private static string NormalizeAssessmentSeverity(RegulatoryEvidenceAssessment assessment)
    {
        return string.Equals(assessment.Severity, "Info", StringComparison.OrdinalIgnoreCase)
            ? "Info"
            : "Warning";
    }

    private static string BuildConclusiveLanguagePattern(string phrase)
    {
        return @"(?<![\p{L}\p{N}_])" +
               BuildDiacriticInsensitivePhraseBody(phrase) +
               @"(?![\p{L}\p{N}_])";
    }

    private static string BuildFraudFamilyPattern()
    {
        return @"(?<![\p{L}\p{N}_])" +
               $"(?:{BuildDiacriticInsensitivePhraseBody("la empresa")}\\s+)?" +
               $"(?:{BuildDiacriticInsensitivePhraseBody("comete")}|" +
               $"{BuildDiacriticInsensitivePhraseBody("cometió")})\\s+" +
               $"(?:{BuildDiacriticInsensitivePhraseBody("un")}\\s+)?" +
               BuildDiacriticInsensitivePhraseBody("fraude") +
               @"(?![\p{L}\p{N}_])";
    }

    private static string BuildConfirmedNonComplianceFamilyPattern()
    {
        return @"(?<![\p{L}\p{N}_])" +
               BuildDiacriticInsensitivePhraseBody("se") + @"\s+" +
               $"(?:{BuildDiacriticInsensitivePhraseBody("confirma")}|" +
               $"{BuildDiacriticInsensitivePhraseBody("confirmó")})\\s+" +
               $"(?:{BuildDiacriticInsensitivePhraseBody("el")}\\s+)?" +
               BuildDiacriticInsensitivePhraseBody("incumplimiento") +
               @"(?![\p{L}\p{N}_])";
    }

    private static string BuildCulpabilityFamilyPattern(string copula, string conclusion)
    {
        return @"(?<![\p{L}\p{N}_])" +
               $"(?:{BuildDiacriticInsensitivePhraseBody(copula)}\\s+)?" +
               BuildDiacriticInsensitivePhraseBody(conclusion) +
               @"(?![\p{L}\p{N}_])";
    }

    private static string BuildDiacriticInsensitivePhraseBody(string phrase)
    {
        var normalized = phrase.Normalize(NormalizationForm.FormD);
        var pattern = new StringBuilder();
        var previousWasWhitespace = false;

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or
                UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    pattern.Append(@"\s+");
                    previousWasWhitespace = true;
                }

                continue;
            }

            previousWasWhitespace = false;
            pattern.Append(Regex.Escape(character.ToString()));
            if (char.IsLetter(character))
            {
                pattern.Append(@"\p{M}*");
            }
        }

        return pattern.ToString();
    }

    private static bool IsProtectedConclusiveLanguageMatch(string value, Match match)
    {
        return IsExplicitlyNegated(value, match.Index) ||
               IsInsideLegalAdviceDisclaimer(value, match);
    }

    private static bool IsExplicitlyNegated(string value, int matchIndex)
    {
        var index = matchIndex - 1;
        while (index >= 0 && value[index] is ' ' or '\t')
        {
            index--;
        }

        if (index < 0 || !char.IsLetter(value[index]))
        {
            return false;
        }

        var tokenEnd = index + 1;
        while (index >= 0 && char.IsLetter(value[index]))
        {
            index--;
        }

        var token = value[(index + 1)..tokenEnd];
        return string.Equals(token, "no", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(token, "not", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInsideLegalAdviceDisclaimer(string value, Match match)
    {
        return Regex.Matches(
                value,
                LegalAdviceDisclaimerPattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Any(disclaimer =>
                match.Index >= disclaimer.Index &&
                match.Index + match.Length <= disclaimer.Index + disclaimer.Length);
    }

    private static bool IsEquivalentLegalAdviceDisclaimer(string value)
    {
        return string.Equals(
            BuildLegalAdviceDisclaimerComparisonKey(value),
            LegalAdviceDisclaimerComparisonKey,
            StringComparison.Ordinal);
    }

    private static string BuildLegalAdviceDisclaimerComparisonKey(string value)
    {
        var key = new StringBuilder();
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or
                UnicodeCategory.EnclosingMark ||
                char.IsWhiteSpace(character) ||
                char.IsPunctuation(character))
            {
                continue;
            }

            key.Append(char.ToLowerInvariant(character));
        }

        return key.ToString().Normalize(NormalizationForm.FormC);
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

    private static bool ContainsForbiddenLanguage(
        LegalAnalysisReviewResult result,
        bool useLegacySubstringMatching)
    {
        var values = useLegacySubstringMatching
            ? EnumerateOutputStrings(result)
            : EnumerateNarrativeStrings(result);

        return values.Any(value =>
            ContainsForbiddenLanguage(value, useLegacySubstringMatching));
    }

    private static bool ContainsForbiddenLanguage(string value, bool useLegacySubstringMatching)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (useLegacySubstringMatching)
        {
            var text = value.ToLowerInvariant();
            return LegalAnalysisAiReviewLanguageRules.ForbiddenLanguage.Any(forbidden =>
                text.Contains(forbidden, StringComparison.Ordinal));
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        return ConclusiveLanguagePatterns.Any(pattern =>
            Regex.Matches(
                    normalized,
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Any(match => !IsProtectedConclusiveLanguageMatch(normalized, match)));
    }

    private static IEnumerable<string> EnumerateNarrativeStrings(LegalAnalysisReviewResult result)
    {
        yield return result.ReviewSummary;

        foreach (var area in result.PossibleRegulatoryReviewAreas)
        {
            yield return area.Title;
            yield return area.Description;
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
            evidenceAssessment = input.EvidenceAssessment == null
                ? null
                : new
                {
                    input.EvidenceAssessment.EvidenceFound,
                    input.EvidenceAssessment.Relevance,
                    input.EvidenceAssessment.Applicability,
                    input.EvidenceAssessment.EvidenceQuality,
                    input.EvidenceAssessment.Severity,
                    input.EvidenceAssessment.RequiresHumanReview,
                    input.EvidenceAssessment.Reasons
                },
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
