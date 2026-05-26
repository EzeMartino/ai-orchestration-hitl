using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Legal.AiReview;
using Orchestration.Infrastructure.Agents.Legal.AiReview;
using Orchestration.Infrastructure.Agents.Planner.Reasoning;
using Xunit;

namespace Orchestration.Tests.Agents.Legal.AiReview;

public sealed class LegalReviewQualityCaseTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DeterministicLegalAnalysisReviewService _deterministic = new();

    [Fact]
    public async Task DeterministicReview_Should_pass_liquidity_risk_with_cnv_evidence_quality_case()
    {
        var qualityCase = LoadCase("liquidity-risk-with-cnv-evidence.json");

        var result = await _deterministic.ReviewAsync(CreateInput(qualityCase), CancellationToken.None);

        result.PossibleRegulatoryReviewAreas.Should().NotBeEmpty();
        result.PossibleRegulatoryReviewAreas[0].Title.Should().Contain("Possible liquidity");
        result.PossibleRegulatoryReviewAreas[0].Description.Should().Contain("human review");
        result.PossibleRegulatoryReviewAreas[0].Severity.Should().Be("High");
        AssertOnlyProvidedCitationsUsed(result, qualityCase.ExpectedCitations);
        AssertDoesNotContainForbiddenLegalLanguage(result);
    }

    [Fact]
    public async Task DeterministicReview_Should_pass_leverage_risk_with_cnv_evidence_quality_case()
    {
        var qualityCase = LoadCase("leverage-risk-with-cnv-evidence.json");

        var result = await _deterministic.ReviewAsync(CreateInput(qualityCase), CancellationToken.None);

        result.PossibleRegulatoryReviewAreas.Should().NotBeEmpty();
        result.PossibleRegulatoryReviewAreas[0].Title.Should().Contain("Possible leverage");
        result.PossibleRegulatoryReviewAreas[0].RelatedFinancialSignals.Should().Contain("HIGH_NET_DEBT_TO_EBITDA");
        result.PossibleRegulatoryReviewAreas[0].Severity.Should().Be("High");
        AssertOnlyProvidedCitationsUsed(result, qualityCase.ExpectedCitations);
        AssertDoesNotContainForbiddenLegalLanguage(result);
    }

    [Fact]
    public async Task DeterministicReview_Should_keep_missing_citations_as_safe_warning()
    {
        var qualityCase = LoadCase("missing-citations.json");

        var result = await _deterministic.ReviewAsync(CreateInput(qualityCase), CancellationToken.None);

        result.PossibleRegulatoryReviewAreas.Should().BeEmpty();
        result.EvidenceReferences.Should().BeEmpty();
        result.Warnings.Should().Contain("Some CNV/Infoleg evidence was ignored because it had no citation.");
        result.Warnings.Should().Contain("Financial risk signals were present, but no cited CNV/Infoleg evidence was available.");
        AssertOnlyProvidedCitationsUsed(result, qualityCase.ExpectedCitations);
        AssertDoesNotContainForbiddenLegalLanguage(result);
    }

    [Fact]
    public async Task DeterministicReview_Should_keep_no_relevant_evidence_as_safe_limitation()
    {
        var qualityCase = LoadCase("no-relevant-evidence.json");

        var result = await _deterministic.ReviewAsync(CreateInput(qualityCase), CancellationToken.None);

        result.PossibleRegulatoryReviewAreas.Should().BeEmpty();
        result.EvidenceReferences.Should().BeEmpty();
        result.Warnings.Should().Contain("No CNV/Infoleg evidence was provided.");
        result.Warnings.Should().Contain("Financial risk signals were present, but no cited CNV/Infoleg evidence was available.");
        AssertOnlyProvidedCitationsUsed(result, qualityCase.ExpectedCitations);
        AssertDoesNotContainForbiddenLegalLanguage(result);
    }

    [Fact]
    public void NotRun_Should_preserve_safe_fallback_metadata()
    {
        var qualityCase = LoadCase("fallback-mode.json");

        var result = LegalAnalysisReviewResults.NotRun(qualityCase.Id);

        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
        result.FailureReason.Should().Be("fallback-mode");
        result.PossibleRegulatoryReviewAreas.Should().BeEmpty();
        result.EvidenceReferences.Should().BeEmpty();
        AssertOnlyProvidedCitationsUsed(result, qualityCase.ExpectedCitations);
        AssertDoesNotContainForbiddenLegalLanguage(result);
    }

    [Fact]
    public async Task SemanticReview_Should_accept_cited_liquidity_evidence_without_inventing_citations()
    {
        var qualityCase = LoadCase("liquidity-risk-with-cnv-evidence.json");
        var response = """
        {
          "reviewSummary": "Possible regulatory review area identified from provided evidence.",
          "possibleRegulatoryReviewAreas": [
            {
              "title": "Possible liquidity review area",
              "description": "The structured liquidity signal may require review together with the cited disclosure evidence.",
              "severity": "High",
              "relatedFinancialSignals": ["LOW_CURRENT_RATIO"],
              "evidenceCitations": ["TEST-CNV-LIQ-001"]
            }
          ],
          "evidenceReferences": [
            {
              "source": "CNV test fixture",
              "title": "Test CNV disclosure evidence",
              "url": "https://example.test/cnv/liquidity",
              "citation": "TEST-CNV-LIQ-001",
              "snippet": "Test fixture excerpt for periodic financial disclosure review.",
              "regulationArea": "financial_reporting",
              "score": 0.91
            }
          ],
          "warnings": [],
          "limitations": ["This is an advisory review over provided evidence."]
        }
        """;
        var service = CreateSemanticService(response);

        var result = await service.ReviewAsync(CreateInput(qualityCase), CancellationToken.None);

        result.UsedLlm.Should().BeTrue();
        result.UsedFallback.Should().BeFalse();
        result.PossibleRegulatoryReviewAreas.Should().ContainSingle();
        AssertOnlyProvidedCitationsUsed(result, qualityCase.ExpectedCitations);
        AssertDoesNotContainForbiddenLegalLanguage(result);
    }

    [Fact]
    public async Task SemanticReview_Should_prune_invented_citation_and_remove_unsupported_area()
    {
        var qualityCase = LoadCase("leverage-risk-with-cnv-evidence.json");
        var response = """
        {
          "reviewSummary": "Citation pruning case.",
          "possibleRegulatoryReviewAreas": [
            {
              "title": "Possible leverage review area",
              "description": "This area includes one provided citation and one invented citation.",
              "severity": "High",
              "relatedFinancialSignals": ["HIGH_NET_DEBT_TO_EBITDA"],
              "evidenceCitations": ["TEST-CNV-LEV-001", "INVENTED-CITATION"]
            },
            {
              "title": "Unsupported area",
              "description": "This area has no provided citation.",
              "severity": "Medium",
              "relatedFinancialSignals": [],
              "evidenceCitations": ["INVENTED-CITATION"]
            }
          ],
          "evidenceReferences": [
            {
              "source": "CNV test fixture",
              "title": "Test CNV indebtedness disclosure evidence",
              "url": "https://example.test/cnv/leverage",
              "citation": "TEST-CNV-LEV-001",
              "snippet": "Test fixture excerpt for indebtedness and market information review.",
              "regulationArea": "debt_leverage",
              "score": 0.89
            },
            {
              "source": "CNV test fixture",
              "title": "Invented reference",
              "url": null,
              "citation": "INVENTED-CITATION",
              "snippet": "Invented reference.",
              "regulationArea": null,
              "score": 0.1
            }
          ],
          "warnings": [],
          "limitations": []
        }
        """;
        var service = CreateSemanticService(response);

        var result = await service.ReviewAsync(CreateInput(qualityCase), CancellationToken.None);

        result.UsedLlm.Should().BeTrue();
        result.PossibleRegulatoryReviewAreas.Should().ContainSingle();
        result.PossibleRegulatoryReviewAreas[0].Title.Should().Be("Possible leverage review area");
        result.Warnings.Should().Contain("Unknown citations or references returned by the AI review were removed.");
        result.Warnings.Should().Contain("Some possible regulatory review areas were removed because they had no valid evidence citations.");
        AssertOnlyProvidedCitationsUsed(result, qualityCase.ExpectedCitations);
        AssertDoesNotContainForbiddenLegalLanguage(result);
    }

    [Fact]
    public async Task SemanticReview_Should_fallback_when_llm_uses_forbidden_legal_language()
    {
        var qualityCase = LoadCase("leverage-risk-with-cnv-evidence.json");
        var response = """
        {
          "reviewSummary": "This is illegal under the provided evidence.",
          "possibleRegulatoryReviewAreas": [
            {
              "title": "Possible leverage review area",
              "description": "Provided evidence citation.",
              "severity": "High",
              "relatedFinancialSignals": ["HIGH_NET_DEBT_TO_EBITDA"],
              "evidenceCitations": ["TEST-CNV-LEV-001"]
            }
          ],
          "evidenceReferences": [],
          "warnings": [],
          "limitations": []
        }
        """;
        var service = CreateSemanticService(response);

        var result = await service.ReviewAsync(CreateInput(qualityCase), CancellationToken.None);

        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
        result.FailureReason.Should().Be("forbidden_language");
        AssertOnlyProvidedCitationsUsed(result, qualityCase.ExpectedCitations);
        AssertDoesNotContainForbiddenLegalLanguage(result);
    }

    [Fact]
    public async Task SemanticReview_Should_keep_no_relevant_evidence_safe_without_supported_areas()
    {
        var qualityCase = LoadCase("no-relevant-evidence.json");
        var response = """
        {
          "reviewSummary": "No cited evidence was available to support review areas.",
          "possibleRegulatoryReviewAreas": [
            {
              "title": "Unsupported area",
              "description": "This area references a citation that was not provided.",
              "severity": "Medium",
              "relatedFinancialSignals": ["HIGH_DEBT_TO_EQUITY"],
              "evidenceCitations": ["INVENTED-CITATION"]
            }
          ],
          "evidenceReferences": [],
          "warnings": ["No relevant cited evidence was provided."],
          "limitations": []
        }
        """;
        var service = CreateSemanticService(response);

        var result = await service.ReviewAsync(CreateInput(qualityCase), CancellationToken.None);

        result.UsedLlm.Should().BeTrue();
        result.PossibleRegulatoryReviewAreas.Should().BeEmpty();
        result.EvidenceReferences.Should().BeEmpty();
        result.Warnings.Should().Contain("Some possible regulatory review areas were removed because they had no valid evidence citations.");
        result.Limitations.Should().Contain("LLM output did not include review areas supported by provided citations.");
        AssertOnlyProvidedCitationsUsed(result, qualityCase.ExpectedCitations);
        AssertDoesNotContainForbiddenLegalLanguage(result);
    }

    private static LegalReviewQualityCase LoadCase(string fileName)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "LegalReviewQuality",
            fileName
        );

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LegalReviewQualityCase>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Fixture {fileName} could not be loaded.");
    }

    private static LegalAnalysisReviewInput CreateInput(LegalReviewQualityCase qualityCase)
    {
        return new LegalAnalysisReviewInput(
            SessionId: "33333333-3333-3333-3333-333333333333",
            Company: qualityCase.Company,
            DocumentId: qualityCase.DocumentId,
            MetricsInputSource: "session_context",
            MetricsProvenance: null,
            FinancialAiReview: CreateFinancialAiReview(qualityCase),
            FinancialRiskSignals: qualityCase.FinancialRiskSignals,
            FinancialRiskEvidence: qualityCase.FinancialRiskSignals
                .SelectMany(signal => signal.Evidence)
                .ToArray(),
            FinancialWarnings: Array.Empty<string>(),
            FinancialLimitations: Array.Empty<string>(),
            CnvEvidence: qualityCase.CnvEvidence
        );
    }

    private static FinancialAnalysisAiReviewResult CreateFinancialAiReview(
        LegalReviewQualityCase qualityCase)
    {
        return new FinancialAnalysisAiReviewResult(
            Summary: "Deterministic financial evidence was available for legal review.",
            KeyFindings: qualityCase.FinancialRiskSignals
                .Select(signal => new FinancialAnalysisAiKeyFinding(
                    Title: signal.Name,
                    Description: signal.Reason ?? signal.Summary,
                    Severity: signal.Severity,
                    RelatedMetrics: string.IsNullOrWhiteSpace(signal.Metric)
                        ? Array.Empty<string>()
                        : [signal.Metric]
                ))
                .ToArray(),
            RiskInterpretation: "Financial risk signals are advisory inputs for human review.",
            DataQualityNotes: Array.Empty<FinancialAnalysisAiDataQualityNote>(),
            Limitations: ["This AI review does not provide legal advice."],
            UsedLlm: false,
            UsedFallback: true,
            Provider: null,
            Model: null,
            FailureReason: null
        );
    }

    private static SemanticKernelLegalAnalysisReviewService CreateSemanticService(string response)
    {
        return new SemanticKernelLegalAnalysisReviewService(
            new LlmOptions
            {
                Enabled = true,
                Provider = "OpenAI",
                Model = "test-model",
                ApiKey = "not-used",
                ServiceId = "test"
            },
            new LegalAgentOptions
            {
                AiReviewEnabled = true
            },
            new DeterministicLegalAnalysisReviewService(),
            new SemanticKernelLegalAnalysisReviewResponseParser(),
            new FakeChatCompletionService(response)
        );
    }

    private static void AssertDoesNotContainForbiddenLegalLanguage(
        LegalAnalysisReviewResult result)
    {
        var textToCheck = string.Join(
            " ",
            new[]
            {
                result.ReviewSummary
            }
            .Concat(result.PossibleRegulatoryReviewAreas.SelectMany(area => new[]
            {
                area.Title,
                area.Description
            }))
            .Concat(result.Warnings)
            .Concat(result.Limitations)
        ).ToLowerInvariant();

        foreach (var forbidden in LegalAnalysisAiReviewLanguageRules.ForbiddenLanguage)
        {
            textToCheck.Should().NotContain(forbidden.ToLowerInvariant());
        }
    }

    private static void AssertOnlyProvidedCitationsUsed(
        LegalAnalysisReviewResult result,
        IReadOnlyList<string> providedCitations)
    {
        var allowed = providedCitations.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var citation in result.PossibleRegulatoryReviewAreas.SelectMany(area => area.EvidenceCitations))
        {
            allowed.Should().Contain(citation);
        }

        foreach (var citation in result.EvidenceReferences
                     .Select(reference => reference.Citation)
                     .Where(citation => !string.IsNullOrWhiteSpace(citation)))
        {
            allowed.Should().Contain(citation!);
        }
    }

    private sealed record LegalReviewQualityCase(
        string Id,
        string Company,
        string DocumentId,
        IReadOnlyList<FinancialRiskSignal> FinancialRiskSignals,
        IReadOnlyList<LegalEvidenceReference> CnvEvidence,
        IReadOnlyList<string> ExpectedCitations
    );

    private sealed class FakeChatCompletionService : IChatCompletionService
    {
        private readonly string _content;

        public FakeChatCompletionService(string content)
        {
            _content = content;
        }

        public IReadOnlyDictionary<string, object?> Attributes { get; } =
            new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ChatMessageContent> response =
            [
                new ChatMessageContent(
                    AuthorRole.Assistant,
                    _content
                )
            ];

            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
