using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;
using Orchestration.Application.Agents.Legal.AiReview;

namespace Orchestration.Tests.Agents.Legal.AiReview;

public sealed class LegalAnalysisReviewContractsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void LegalAnalysisReviewInput_Should_round_trip_as_json()
    {
        var input = CreateInput();

        var json = JsonSerializer.Serialize(input, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<LegalAnalysisReviewInput>(
            json,
            JsonOptions
        );

        json.Should().Contain("\"metricsInputSource\":\"session_context\"");
        json.Should().Contain("\"metricsProvenance\"");
        json.Should().Contain("\"financialAiReview\"");
        roundTripped.Should().BeEquivalentTo(input);
    }

    [Fact]
    public void LegalAnalysisReviewResult_Should_round_trip_as_json()
    {
        var result = CreateResult();

        var json = JsonSerializer.Serialize(result, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<LegalAnalysisReviewResult>(
            json,
            JsonOptions
        );

        json.Should().Contain("\"usedLlm\":true");
        json.Should().Contain("\"usedFallback\":false");
        json.Should().Contain("\"possibleRegulatoryReviewAreas\"");
        roundTripped.Should().BeEquivalentTo(result);
    }

    [Fact]
    public void PossibleRegulatoryReviewArea_Should_preserve_severity_related_signals_and_evidence_citations()
    {
        var result = CreateResult();

        var area = result.PossibleRegulatoryReviewAreas.Should().ContainSingle().Subject;

        area.Title.Should().Be("CNV Liquidity Compliance");
        area.Severity.Should().Be("High");
        area.RelatedFinancialSignals.Should().BeEquivalentTo(["LOW_CURRENT_RATIO"]);
        area.EvidenceCitations.Should().BeEquivalentTo(["CNV G.C. Art. 42"]);
    }

    [Fact]
    public void LegalEvidenceReference_Should_preserve_citation_source_and_title()
    {
        var result = CreateResult();

        var reference = result.EvidenceReferences.Should().ContainSingle().Subject;

        reference.Source.Should().Be("CNV");
        reference.Title.Should().Be("CNV Rules Chapter III Section 4");
        reference.Citation.Should().Be("CNV G.C. Art. 42");
        reference.Snippet.Should().Be("Entities must maintain a current ratio above 1.0 at all times.");
        reference.RegulationArea.Should().Be("Liquidity");
        reference.Score.Should().Be(0.95);
    }

    [Fact]
    public void NotRun_Should_set_fallback_metadata()
    {
        var result = LegalAnalysisReviewResults.NotRun("Legal AI review is disabled.");

        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
        result.Provider.Should().BeNull();
        result.Model.Should().BeNull();
        result.FailureReason.Should().Be("Legal AI review is disabled.");
        result.ReviewSummary.Should().Be("La revisión legal de IA no se ejecutó.");
        result.Limitations.Should().Contain("La revisión legal de IA no se ejecutó.");
    }

    [Fact]
    public void NotRun_Should_include_legal_advice_limitation()
    {
        var result = LegalAnalysisReviewResults.NotRun("Not run testing.");

        result.Limitations.Should().Contain("Este sistema no brinda asesoramiento legal.");
        result.Limitations.Should().Contain("No se proporciona ninguna conclusión legal o regulatoria definitiva.");
    }

    [Fact]
    public void NotRun_Should_not_include_forbidden_definitive_legal_language()
    {
        var result = LegalAnalysisReviewResults.NotRun("Not run testing.");

        var textToCheck = (result.ReviewSummary + " " + string.Join(" ", result.Limitations)).ToLowerInvariant();

        foreach (var forbidden in LegalAnalysisAiReviewLanguageRules.ForbiddenLanguage)
        {
            textToCheck.Should().NotContain(forbidden.ToLowerInvariant());
        }
    }

    [Fact]
    public void Empty_collections_Should_deserialize_safely()
    {
        const string json = """
        {
          "reviewSummary": "La revisión legal de IA no se ejecutó.",
          "possibleRegulatoryReviewAreas": [],
          "evidenceReferences": [],
          "warnings": [],
          "limitations": [],
          "usedLlm": false,
          "usedFallback": true,
          "provider": null,
          "model": null,
          "failureReason": "Not configured."
        }
        """;

        var result = JsonSerializer.Deserialize<LegalAnalysisReviewResult>(
            json,
            JsonOptions
        );

        result.Should().NotBeNull();
        result!.PossibleRegulatoryReviewAreas.Should().BeEmpty();
        result.EvidenceReferences.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.Limitations.Should().BeEmpty();
        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
    }

    private static LegalAnalysisReviewInput CreateInput()
    {
        var evidence = new RiskEvidenceItem(
            MetricName: "current_ratio",
            Period: "2025E",
            Value: 0.67m,
            Threshold: 1.0m,
            Unit: "ratio",
            Interpretation: "Current ratio is below threshold."
        );

        var financialAiReview = new FinancialAnalysisAiReviewResult(
            Summary: "Financial risk signals require human review.",
            KeyFindings:
            [
                new FinancialAnalysisAiKeyFinding(
                    Title: "Liquidity pressure",
                    Description: "Current ratio is below threshold.",
                    Severity: "High",
                    RelatedMetrics: ["current_ratio"]
                )
            ],
            RiskInterpretation: "Quantitative risk signals indicate review is warranted.",
            DataQualityNotes:
            [
                new FinancialAnalysisAiDataQualityNote(
                    Message: "Structured metrics are manually provided.",
                    Severity: "Warning",
                    RelatedFields: ["metrics"]
                )
            ],
            Limitations: ["AI review is advisory."],
            UsedLlm: true,
            UsedFallback: false,
            Provider: "OpenAI",
            Model: "gpt-4o",
            FailureReason: null
        );

        var legalEvidence = new LegalEvidenceReference(
            Source: "CNV",
            Title: "CNV Rules Chapter III Section 4",
            Url: "https://www.cnv.gov.ar/rules/ch3sec4",
            Citation: "CNV G.C. Art. 42",
            Snippet: "Entities must maintain a current ratio above 1.0 at all times.",
            RegulationArea: "Liquidity",
            Score: 0.95
        );

        return new LegalAnalysisReviewInput(
            SessionId: "22222222-2222-2222-2222-222222222222",
            Company: "Manual Test Co",
            DocumentId: "manual-json-input",
            MetricsInputSource: "session_context",
            MetricsProvenance: new StructuredFinancialMetricsProvenance(
                IngestionMethod: "json_paste",
                OriginalFileName: null,
                FileSizeBytes: null,
                ContentHash: null,
                MetricCount: 10,
                WarningCount: 1
            ),
            FinancialAiReview: financialAiReview,
            FinancialRiskSignals:
            [
                new FinancialRiskSignal(
                    Name: "LOW_CURRENT_RATIO",
                    Severity: "High",
                    Period: "2025E",
                    Summary: "Current ratio below threshold.",
                    Evidence: [evidence]
                )
            ],
            FinancialRiskEvidence: [evidence],
            FinancialWarnings: ["Structured metrics are manually provided."],
            FinancialLimitations: ["AI review must not recompute financial metrics."],
            CnvEvidence: [legalEvidence]
        );
    }

    private static LegalAnalysisReviewResult CreateResult()
    {
        var legalEvidence = new LegalEvidenceReference(
            Source: "CNV",
            Title: "CNV Rules Chapter III Section 4",
            Url: "https://www.cnv.gov.ar/rules/ch3sec4",
            Citation: "CNV G.C. Art. 42",
            Snippet: "Entities must maintain a current ratio above 1.0 at all times.",
            RegulationArea: "Liquidity",
            Score: 0.95
        );

        return new LegalAnalysisReviewResult(
            ReviewSummary: "Legal AI review analyzed DataAgent results and CNV regulations.",
            PossibleRegulatoryReviewAreas:
            [
                new PossibleRegulatoryReviewArea(
                    Title: "CNV Liquidity Compliance",
                    Description: "The current ratio of 0.67 is below the required 1.0 threshold mandated by CNV G.C. Art. 42.",
                    Severity: "High",
                    RelatedFinancialSignals: ["LOW_CURRENT_RATIO"],
                    EvidenceCitations: ["CNV G.C. Art. 42"]
                )
            ],
            EvidenceReferences: [legalEvidence],
            Warnings: ["Potential compliance risk detected."],
            Limitations: ["Legal review is advisory and does not constitute formal legal advice."],
            UsedLlm: true,
            UsedFallback: false,
            Provider: "OpenAI",
            Model: "gpt-4o",
            FailureReason: null
        );
    }
}
