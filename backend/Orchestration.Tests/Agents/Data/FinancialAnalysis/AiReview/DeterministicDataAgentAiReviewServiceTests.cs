using FluentAssertions;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis.AiReview;

public sealed class DeterministicDataAgentAiReviewServiceTests
{
    [Fact]
    public async Task ReviewAsync_Should_return_fallback_metadata_without_provider_or_model()
    {
        var service = new DeterministicDataAgentAiReviewService();

        var result = await service.ReviewAsync(
            CreateInput(riskSignals: [CreateRiskSignal("HIGH_LEVERAGE", "High", "net_debt_to_ebitda")]),
            CancellationToken.None
        );

        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
        result.Provider.Should().BeNull();
        result.Model.Should().BeNull();
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task ReviewAsync_Should_create_summary_from_risk_signals()
    {
        var service = new DeterministicDataAgentAiReviewService();

        var result = await service.ReviewAsync(
            CreateInput(riskSignals:
            [
                CreateRiskSignal("LOW_CURRENT_RATIO", "High", "current_ratio"),
                CreateRiskSignal("NEGATIVE_FREE_CASH_FLOW", "Medium", "free_cash_flow")
            ]),
            CancellationToken.None
        );

        result.Summary.Should().Contain("identified 2 risk signal(s)");
        result.Summary.Should().Contain("metrics attached to the analysis session");
    }

    [Fact]
    public async Task ReviewAsync_Should_create_empty_key_findings_when_no_risk_signals()
    {
        var service = new DeterministicDataAgentAiReviewService();

        var result = await service.ReviewAsync(
            CreateInput(riskSignals: []),
            CancellationToken.None
        );

        result.Summary.Should().Contain("did not identify major risk signals");
        result.KeyFindings.Should().BeEmpty();
    }

    [Fact]
    public async Task ReviewAsync_Should_create_one_key_finding_per_risk_signal()
    {
        var service = new DeterministicDataAgentAiReviewService();

        var result = await service.ReviewAsync(
            CreateInput(riskSignals:
            [
                CreateRiskSignal("LOW_CURRENT_RATIO", "High", "current_ratio"),
                CreateRiskSignal("HIGH_DEBT_TO_EQUITY", "Medium", "debt_to_equity")
            ]),
            CancellationToken.None
        );

        result.KeyFindings.Should().HaveCount(2);
        result.KeyFindings.Select(finding => finding.Title)
            .Should()
            .BeEquivalentTo(["LOW_CURRENT_RATIO", "HIGH_DEBT_TO_EQUITY"]);
    }

    [Fact]
    public async Task ReviewAsync_Should_preserve_risk_signal_severity()
    {
        var service = new DeterministicDataAgentAiReviewService();

        var result = await service.ReviewAsync(
            CreateInput(riskSignals: [CreateRiskSignal("LOW_CURRENT_RATIO", "High", "current_ratio")]),
            CancellationToken.None
        );

        result.KeyFindings.Should().ContainSingle()
            .Which.Severity.Should().Be("High");
    }

    [Fact]
    public async Task ReviewAsync_Should_not_invent_related_metrics()
    {
        var service = new DeterministicDataAgentAiReviewService();

        var result = await service.ReviewAsync(
            CreateInput(riskSignals:
            [
                new FinancialRiskSignal(
                    Name: "LOW_CURRENT_RATIO",
                    Severity: "High",
                    Period: "2025E",
                    Summary: "Current ratio is below threshold.",
                    Evidence:
                    [
                        CreateEvidence("current_ratio"),
                        CreateEvidence("current_ratio")
                    ]
                ),
                new FinancialRiskSignal(
                    Name: "DATA_QUALITY_WATCH",
                    Severity: "Info",
                    Period: "2025E",
                    Summary: "Signal has no metric evidence.",
                    Evidence: []
                )
            ]),
            CancellationToken.None
        );

        result.KeyFindings[0].RelatedMetrics.Should().BeEquivalentTo(["current_ratio"]);
        result.KeyFindings[1].RelatedMetrics.Should().BeEmpty();
    }

    [Fact]
    public async Task ReviewAsync_Should_include_warnings_as_data_quality_notes()
    {
        var service = new DeterministicDataAgentAiReviewService();

        var result = await service.ReviewAsync(
            CreateInput(
                riskSignals: [],
                warnings: ["Structured metrics are manually provided."]
            ),
            CancellationToken.None
        );

        result.DataQualityNotes.Should().Contain(note =>
            note.Message == "Structured metrics are manually provided." &&
            note.Severity == "Warning"
        );
    }

    [Fact]
    public async Task ReviewAsync_Should_include_input_and_deterministic_limitations()
    {
        var service = new DeterministicDataAgentAiReviewService();

        var result = await service.ReviewAsync(
            CreateInput(
                riskSignals: [],
                limitations: ["No PDF extraction was performed."]
            ),
            CancellationToken.None
        );

        result.Limitations.Should().Contain("No PDF extraction was performed.");
        result.Limitations.Should().Contain("This review is deterministic and advisory.");
        result.Limitations.Should().Contain("It does not recompute financial metrics.");
        result.Limitations.Should().Contain("It does not verify accounting records.");
        result.Limitations.Should().Contain("It does not provide portfolio or transaction recommendations.");
    }

    [Fact]
    public async Task ReviewAsync_Should_handle_session_context_source()
    {
        var service = new DeterministicDataAgentAiReviewService();

        var result = await service.ReviewAsync(
            CreateInput(
                riskSignals: [],
                metricsInputSource: FinancialMetricsInputSources.SessionContext
            ),
            CancellationToken.None
        );

        result.Summary.Should().Contain("metrics attached to the analysis session");
        result.DataQualityNotes.Should().NotContain(note =>
            note.Message.Contains("Fixture fallback", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task ReviewAsync_Should_handle_fixture_fallback_source_with_warning_note()
    {
        var service = new DeterministicDataAgentAiReviewService();

        var result = await service.ReviewAsync(
            CreateInput(
                riskSignals: [],
                metricsInputSource: FinancialMetricsInputSources.FixtureFallback
            ),
            CancellationToken.None
        );

        result.Summary.Should().Contain("fixture fallback metrics");
        result.DataQualityNotes.Should().Contain(note =>
            note.Message == "Fixture fallback metrics were used. This is intended for demo/development only." &&
            note.Severity == "Warning"
        );
    }

    [Fact]
    public async Task ReviewAsync_Should_handle_none_source_with_data_quality_note()
    {
        var service = new DeterministicDataAgentAiReviewService();

        var result = await service.ReviewAsync(
            CreateInput(
                riskSignals: [],
                metricsInputSource: FinancialMetricsInputSources.None
            ),
            CancellationToken.None
        );

        result.Summary.Should().Be("Structured financial metrics were not available for review.");
        result.DataQualityNotes.Should().Contain(note =>
            note.Message == "No structured financial metrics were available for review." &&
            note.Severity == "Warning"
        );
    }

    [Fact]
    public async Task ReviewAsync_Should_include_ingestion_warning_count_note()
    {
        var service = new DeterministicDataAgentAiReviewService();

        var result = await service.ReviewAsync(
            CreateInput(
                riskSignals: [],
                provenance: new StructuredFinancialMetricsProvenance(
                    IngestionMethod: "json_file",
                    OriginalFileName: "metrics.json",
                    FileSizeBytes: 1024,
                    ContentHash: "abc123",
                    MetricCount: 12,
                    WarningCount: 3
                )
            ),
            CancellationToken.None
        );

        result.DataQualityNotes.Should().Contain(note =>
            note.Message == "The structured metrics ingestion produced 3 warning(s)." &&
            note.RelatedFields.Contains("metricsProvenance.warningCount")
        );
    }

    [Fact]
    public async Task ReviewAsync_Should_not_include_disallowed_advice_or_definitive_language()
    {
        var service = new DeterministicDataAgentAiReviewService();

        var result = await service.ReviewAsync(
            CreateInput(riskSignals: [CreateRiskSignal("LOW_CURRENT_RATIO", "High", "current_ratio")]),
            CancellationToken.None
        );

        var output = string.Join(
            " ",
            EnumerateOutputStrings(result)
        ).ToLowerInvariant();

        output.Should().NotContain("investment advice");
        output.Should().NotContain("buy");
        output.Should().NotContain("sell");
        output.Should().NotContain("illegal");
        output.Should().NotContain("guaranteed");
        output.Should().NotContain("accounting correctness confirmed");
    }

    [Fact]
    public async Task ReviewAsync_Should_not_modify_input_collections()
    {
        var service = new DeterministicDataAgentAiReviewService();
        var warning = "Original warning.";
        var limitation = "Original limitation.";
        var riskSignal = CreateRiskSignal("LOW_CURRENT_RATIO", "High", "current_ratio");
        var input = CreateInput(
            riskSignals: [riskSignal],
            warnings: [warning],
            limitations: [limitation]
        );

        await service.ReviewAsync(input, CancellationToken.None);

        input.RiskSignals.Should().ContainSingle().Which.Should().Be(riskSignal);
        input.Warnings.Should().BeEquivalentTo([warning]);
        input.Limitations.Should().BeEquivalentTo([limitation]);
    }

    private static FinancialAnalysisAiReviewInput CreateInput(
        IReadOnlyList<FinancialRiskSignal>? riskSignals = null,
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<string>? limitations = null,
        string? metricsInputSource = FinancialMetricsInputSources.SessionContext,
        StructuredFinancialMetricsProvenance? provenance = null)
    {
        return new FinancialAnalysisAiReviewInput(
            SessionId: "11111111-1111-1111-1111-111111111111",
            DocumentId: "manual-json-input",
            Company: "Manual Test Co",
            MetricsInputSource: metricsInputSource,
            MetricsProvenance: provenance,
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
            PeriodComparisons: [],
            RiskSignals: riskSignals ?? [],
            RiskEvidence: [],
            Warnings: warnings ?? [],
            Limitations: limitations ?? []
        );
    }

    private static FinancialRiskSignal CreateRiskSignal(
        string name,
        string severity,
        string metricName)
    {
        return new FinancialRiskSignal(
            Name: name,
            Severity: severity,
            Period: "2025E",
            Summary: $"{metricName} requires human review.",
            Evidence: [CreateEvidence(metricName)]
        );
    }

    private static RiskEvidenceItem CreateEvidence(string metricName)
    {
        return new RiskEvidenceItem(
            MetricName: metricName,
            Period: "2025E",
            Value: 0.67m,
            Threshold: 1.0m,
            Unit: "ratio",
            Interpretation: $"{metricName} crossed the configured review threshold."
        );
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
}
