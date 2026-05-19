using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Shared;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class DataAgentFinancialAnalysisWorkflowTests
{
    [Fact]
    public async Task AnalyzeAsync_Should_return_data_agent_result_from_financial_risk_signals()
    {
        var provider = new FakeStructuredFinancialMetricsProvider(CreateMetricsDocument());
        var service = new FakePythonFinancialAnalysisService
        {
            RatioWarnings = ["Structured metrics only."],
            SignalWarnings = ["Human review recommended."]
        };
        var workflow = CreateWorkflow(provider, service);

        var result = await workflow.AnalyzeAsync(
            CreateReport(),
            CancellationToken.None
        );

        result.Engine.Should().Be("Semantic Kernel + CSnakes + Python/Pandas");
        result.HasAnomaly.Should().BeTrue();
        result.Severity.Should().Be("High");
        result.Summary.Should().Contain("High-severity quantitative evidence summarized.");
        result.Summary.Should().Contain("Structured metrics only.");
        result.Evidence.Should().Contain(evidence =>
            evidence.Metric == "net_debt_to_ebitda" &&
            evidence.Value == 3.75 &&
            evidence.Threshold == 3.0
        );
        result.Evidence.Should().Contain(evidence =>
            evidence.Metric == "FinancialAnalysisWarning" &&
            evidence.Interpretation.Contains("Human review recommended.", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task AnalyzeAsync_Should_return_safe_result_when_metrics_are_unavailable()
    {
        var provider = new FakeStructuredFinancialMetricsProvider(null);
        var service = new FakePythonFinancialAnalysisService();
        var workflow = CreateWorkflow(provider, service);

        var result = await workflow.AnalyzeAsync(
            CreateReport(),
            CancellationToken.None
        );

        result.Engine.Should().Be("Semantic Kernel + CSnakes + Python/Pandas");
        result.HasAnomaly.Should().BeTrue();
        result.Severity.Should().Be("Medium");
        result.Summary.Should().Be("Structured financial metrics were not available. Human review recommended.");
        result.Evidence.Should().ContainSingle()
            .Which.Metric.Should().Be("StructuredFinancialMetrics");
        service.ComputeCalls.Should().Be(0);
    }

    private static DataAgentFinancialAnalysisWorkflow CreateWorkflow(
        FakeStructuredFinancialMetricsProvider provider,
        FakePythonFinancialAnalysisService service)
    {
        return new DataAgentFinancialAnalysisWorkflow(
            provider,
            service,
            Options.Create(new DataAgentOptions()),
            NullLogger<DataAgentFinancialAnalysisWorkflow>.Instance
        );
    }

    private static FinancialReportContext CreateReport()
    {
        return new FinancialReportContext(
            SessionId: Guid.NewGuid(),
            ReportName: "vista-energy-fixture",
            TotalAmount: 125000m,
            TransactionCount: 42,
            SubmittedAt: DateTimeOffset.UtcNow
        );
    }

    private static StructuredFinancialMetricsDocument CreateMetricsDocument()
    {
        return new StructuredFinancialMetricsDocument(
            DocumentId: "vista-energy-fixture",
            Company: "Vista Energy",
            Currency: "USD",
            Unit: "USD millions",
            Metrics:
            [
                CreateMetric("revenue", "2024A", 100m),
                CreateMetric("revenue", "2025E", 80m),
                CreateMetric("ebitda", "2025E", 12m),
                CreateMetric("net_debt", "2025E", 45m)
            ]
        );
    }

    private static FinancialMetric CreateMetric(
        string name,
        string period,
        decimal value)
    {
        return new FinancialMetric(
            Name: name,
            Period: period,
            Value: value,
            Unit: "USD millions",
            Statement: "unit_test",
            Source: "unit_test"
        );
    }

    private sealed class FakeStructuredFinancialMetricsProvider : IStructuredFinancialMetricsProvider
    {
        private readonly StructuredFinancialMetricsDocument? _document;

        public FakeStructuredFinancialMetricsProvider(
            StructuredFinancialMetricsDocument? document)
        {
            _document = document;
        }

        public Task<StructuredFinancialMetricsDocument?> GetMetricsAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_document);
        }
    }

    private sealed class FakePythonFinancialAnalysisService : IPythonFinancialAnalysisService
    {
        public int ComputeCalls { get; private set; }

        public IReadOnlyList<string> RatioWarnings { get; init; } = [];

        public IReadOnlyList<string> SignalWarnings { get; init; } = [];

        public Task<ComputeFinancialRatiosResponse> ComputeFinancialRatiosAsync(
            ComputeFinancialRatiosRequest request,
            CancellationToken cancellationToken)
        {
            ComputeCalls++;

            return Task.FromResult(new ComputeFinancialRatiosResponse(
                Engine: "Fake Financial Analysis",
                Ratios:
                [
                    new FinancialRatio(
                        Name: "net_debt_to_ebitda",
                        Period: "2025E",
                        Value: 3.75m,
                        Unit: "x",
                        Formula: "net_debt / ebitda",
                        Inputs: ["net_debt", "ebitda"],
                        Interpretation: "Leverage ratio computed."
                    )
                ],
                Warnings: RatioWarnings
            ));
        }

        public Task<ComparePeriodsResponse> ComparePeriodsAsync(
            ComparePeriodsRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ComparePeriodsResponse(
                Engine: "Fake Financial Analysis",
                Comparisons:
                [
                    new FinancialPeriodComparison(
                        MetricName: "revenue",
                        FromPeriod: "2024A",
                        ToPeriod: "2025E",
                        FromValue: 100m,
                        ToValue: 80m,
                        AbsoluteChange: -20m,
                        PercentageChange: -0.2m,
                        Unit: "USD millions",
                        Interpretation: "Revenue dropped."
                    )
                ],
                Warnings: []
            ));
        }

        public Task<DetectFinancialRiskSignalsResponse> DetectFinancialRiskSignalsAsync(
            DetectFinancialRiskSignalsRequest request,
            CancellationToken cancellationToken)
        {
            var leverageEvidence = new RiskEvidenceItem(
                MetricName: "net_debt_to_ebitda",
                Period: "2025E",
                Value: 3.75m,
                Threshold: 3.0m,
                Unit: "x",
                Interpretation: "Leverage requires review."
            );
            var liquidityEvidence = new RiskEvidenceItem(
                MetricName: "current_ratio",
                Period: "2025E",
                Value: 0.8m,
                Threshold: 1.0m,
                Unit: "x",
                Interpretation: "Liquidity requires review."
            );
            var signals = new[]
            {
                new FinancialRiskSignal(
                    Name: "LOW_CURRENT_RATIO",
                    Severity: "Medium",
                    Period: "2025E",
                    Summary: "Liquidity should be reviewed.",
                    Evidence: [liquidityEvidence]
                ),
                new FinancialRiskSignal(
                    Name: "HIGH_NET_DEBT_TO_EBITDA",
                    Severity: "High",
                    Period: "2025E",
                    Summary: "Leverage should be reviewed.",
                    Evidence: [leverageEvidence]
                )
            };

            return Task.FromResult(new DetectFinancialRiskSignalsResponse(
                Engine: "Fake Financial Analysis",
                Signals: signals,
                Result: new FinancialAnalysisToolResult(
                    HasRiskSignals: true,
                    RiskLevel: "High",
                    Summary: "Financial risk signals detected.",
                    Engine: "Fake Financial Analysis",
                    Evidence: [leverageEvidence, liquidityEvidence],
                    Warnings: SignalWarnings
                )
            ));
        }

        public Task<SummarizeQuantitativeEvidenceResponse> SummarizeQuantitativeEvidenceAsync(
            SummarizeQuantitativeEvidenceRequest request,
            CancellationToken cancellationToken)
        {
            var evidence = new RiskEvidenceItem(
                MetricName: "net_debt_to_ebitda",
                Period: "2025E",
                Value: 3.75m,
                Threshold: 3.0m,
                Unit: "x",
                Interpretation: "Leverage requires review."
            );

            return Task.FromResult(new SummarizeQuantitativeEvidenceResponse(
                Engine: "Fake Financial Analysis",
                Narrative: "High-severity quantitative evidence summarized.",
                Result: new FinancialAnalysisToolResult(
                    HasRiskSignals: true,
                    RiskLevel: "High",
                    Summary: "High-severity quantitative evidence summarized.",
                    Engine: "Fake Financial Analysis",
                    Evidence: [evidence],
                    Warnings: []
                )
            ));
        }
    }
}
