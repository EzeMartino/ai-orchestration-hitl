using System.Text.Json;
using FluentAssertions;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis.AiReview;

public sealed class FinancialAnalysisAiReviewContractsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void FinancialAnalysisAiReviewInput_Should_round_trip_as_json()
    {
        var input = CreateInput();

        var json = JsonSerializer.Serialize(input, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<FinancialAnalysisAiReviewInput>(
            json,
            JsonOptions
        );

        json.Should().Contain("\"metricsInputSource\":\"session_context\"");
        json.Should().Contain("\"metricsProvenance\"");
        roundTripped.Should().BeEquivalentTo(input);
    }

    [Fact]
    public void FinancialAnalysisAiReviewResult_Should_round_trip_as_json()
    {
        var result = new FinancialAnalysisAiReviewResult(
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
            Model: "gpt-5.4-nano",
            FailureReason: null
        );

        var json = JsonSerializer.Serialize(result, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<FinancialAnalysisAiReviewResult>(
            json,
            JsonOptions
        );

        json.Should().Contain("\"usedLlm\":true");
        json.Should().Contain("\"usedFallback\":false");
        roundTripped.Should().BeEquivalentTo(result);
    }

    [Fact]
    public void KeyFindings_Should_preserve_severity_and_related_metrics()
    {
        var result = CreateResult();

        var finding = result.KeyFindings.Should().ContainSingle().Subject;

        finding.Severity.Should().Be("Medium");
        finding.RelatedMetrics.Should().BeEquivalentTo(["net_debt_to_ebitda", "debt_to_equity"]);
    }

    [Fact]
    public void DataQualityNotes_Should_preserve_severity_and_related_fields()
    {
        var result = CreateResult();

        var note = result.DataQualityNotes.Should().ContainSingle().Subject;

        note.Severity.Should().Be("Info");
        note.RelatedFields.Should().BeEquivalentTo(["warnings", "limitations"]);
    }

    [Fact]
    public void NotRun_Should_set_fallback_metadata()
    {
        var result = FinancialAnalysisAiReviewResults.NotRun("AI review is disabled.");

        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
        result.Provider.Should().BeNull();
        result.Model.Should().BeNull();
        result.FailureReason.Should().Be("AI review is disabled.");
        result.Summary.Should().Be("AI review was not executed.");
        result.Limitations.Should().Contain("AI review was not executed.");
    }

    [Fact]
    public void Empty_collections_Should_deserialize_as_empty_collections()
    {
        const string json = """
        {
          "summary": "AI review was not executed.",
          "keyFindings": [],
          "riskInterpretation": "No AI interpretation is available.",
          "dataQualityNotes": [],
          "limitations": [],
          "usedLlm": false,
          "usedFallback": true,
          "provider": null,
          "model": null,
          "failureReason": "Not configured."
        }
        """;

        var result = JsonSerializer.Deserialize<FinancialAnalysisAiReviewResult>(
            json,
            JsonOptions
        );

        result.Should().NotBeNull();
        result!.KeyFindings.Should().BeEmpty();
        result.DataQualityNotes.Should().BeEmpty();
        result.Limitations.Should().BeEmpty();
        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
    }

    private static FinancialAnalysisAiReviewInput CreateInput()
    {
        var evidence = new RiskEvidenceItem(
            MetricName: "current_ratio",
            Period: "2025E",
            Value: 0.67m,
            Threshold: 1.0m,
            Unit: "ratio",
            Interpretation: "Current ratio is below threshold."
        );

        return new FinancialAnalysisAiReviewInput(
            SessionId: "11111111-1111-1111-1111-111111111111",
            DocumentId: "manual-json-input",
            Company: "Manual Test Co",
            MetricsInputSource: FinancialMetricsInputSources.SessionContext,
            MetricsProvenance: new StructuredFinancialMetricsProvenance(
                IngestionMethod: "json_paste",
                OriginalFileName: null,
                FileSizeBytes: null,
                ContentHash: null,
                MetricCount: 10,
                WarningCount: 1
            ),
            Ratios:
            [
                new FinancialRatio(
                    Name: "current_ratio",
                    Period: "2025E",
                    Value: 0.67m,
                    Unit: "ratio",
                    Formula: "current_assets / current_liabilities",
                    Inputs: ["current_assets", "current_liabilities"],
                    Interpretation: "Liquidity is below threshold."
                )
            ],
            PeriodComparisons:
            [
                new FinancialPeriodComparison(
                    MetricName: "revenue",
                    FromPeriod: "2024A",
                    ToPeriod: "2025E",
                    FromValue: 100m,
                    ToValue: 82m,
                    AbsoluteChange: -18m,
                    PercentageChange: -0.18m,
                    Unit: "USD_thousand",
                    Interpretation: "Revenue declined."
                )
            ],
            RiskSignals:
            [
                new FinancialRiskSignal(
                    Name: "LOW_CURRENT_RATIO",
                    Severity: "High",
                    Period: "2025E",
                    Summary: "Current ratio below threshold.",
                    Evidence: [evidence]
                )
            ],
            RiskEvidence: [evidence],
            Warnings: ["Structured metrics are manually provided."],
            Limitations: ["AI review must not recompute financial metrics."]
        );
    }

    private static FinancialAnalysisAiReviewResult CreateResult()
    {
        return new FinancialAnalysisAiReviewResult(
            Summary: "AI review summarized deterministic financial evidence.",
            KeyFindings:
            [
                new FinancialAnalysisAiKeyFinding(
                    Title: "Leverage watch",
                    Description: "Leverage metrics should be reviewed.",
                    Severity: "Medium",
                    RelatedMetrics: ["net_debt_to_ebitda", "debt_to_equity"]
                )
            ],
            RiskInterpretation: "Risk signals are advisory and require human review.",
            DataQualityNotes:
            [
                new FinancialAnalysisAiDataQualityNote(
                    Message: "Warnings and limitations should be reviewed.",
                    Severity: "Info",
                    RelatedFields: ["warnings", "limitations"]
                )
            ],
            Limitations: ["AI review is advisory."],
            UsedLlm: false,
            UsedFallback: true,
            Provider: null,
            Model: null,
            FailureReason: "Not configured."
        );
    }
}
