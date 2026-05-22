using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;
using Orchestration.Application.Agents.Shared;
using Orchestration.Application.FinancialAnalysis.Thresholds;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis;
using Orchestration.Tests.Agents;

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
        var aiReviewService = new FakeDataAgentAiReviewService();
        var workflow = CreateWorkflow(
            provider,
            service,
            aiReviewService: aiReviewService
        );

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
        result.Evidence.Should().NotContain(evidence =>
            evidence.Metric == "FinancialAnalysisWarning"
        );
        result.FinancialAnalysis.Should().NotBeNull();
        result.FinancialAnalysis!.DocumentId.Should().Be("vista-energy-fixture");
        result.FinancialAnalysis.Company.Should().Be("Vista Energy");
        result.FinancialAnalysis.Ratios.Should().ContainSingle();
        result.FinancialAnalysis.Comparisons.Should().ContainSingle();
        result.FinancialAnalysis.RiskSignals.Should().HaveCount(2);
        result.FinancialAnalysis.RiskEvidence.Should().Contain(evidence =>
            evidence.MetricName == "net_debt_to_ebitda"
        );
        result.FinancialAnalysis.Warnings.Should().Contain("Structured metrics only.");
        result.FinancialAnalysis.Warnings.Should().Contain("Human review recommended.");
        result.FinancialAnalysis.Limitations.Should().Contain(limitation =>
            limitation.Contains("structured metrics only", StringComparison.OrdinalIgnoreCase)
        );
        result.FinancialAnalysis.AiReview.Should().NotBeNull();
        result.FinancialAnalysis.AiReview!.UsedLlm.Should().BeFalse();
        result.FinancialAnalysis.AiReview.UsedFallback.Should().BeTrue();
        aiReviewService.Calls.Should().Be(1);
        aiReviewService.LastInput.Should().NotBeNull();
        aiReviewService.LastInput!.DocumentId.Should().Be("vista-energy-fixture");
        aiReviewService.LastInput.Ratios.Should().BeEquivalentTo(result.FinancialAnalysis.Ratios);
        aiReviewService.LastInput.PeriodComparisons.Should().BeEquivalentTo(result.FinancialAnalysis.Comparisons);
        aiReviewService.LastInput.RiskSignals.Should().BeEquivalentTo(result.FinancialAnalysis.RiskSignals);
        aiReviewService.LastInput.RiskEvidence.Should().BeEquivalentTo(result.FinancialAnalysis.RiskEvidence);
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
        result.FinancialAnalysis.Should().NotBeNull();
        result.FinancialAnalysis!.Warnings.Should().Contain("Structured financial metrics were not available.");
        result.FinancialAnalysis.Limitations.Should().NotBeEmpty();
        result.FinancialAnalysis.MetricsInputSource.Should().Be(FinancialMetricsInputSources.None);
        result.FinancialAnalysis.AiReview.Should().NotBeNull();
        result.FinancialAnalysis.AiReview!.UsedLlm.Should().BeFalse();
        result.FinancialAnalysis.AiReview.UsedFallback.Should().BeTrue();
        result.FinancialAnalysis.AiReview.FailureReason.Should().Be("structured_financial_metrics_missing");
        service.ComputeCalls.Should().Be(0);
    }

    [Fact]
    public async Task AnalyzeAsync_Should_return_required_metrics_result_when_metrics_are_missing_and_required()
    {
        var publisher = new FakeActivityEventPublisher();
        var provider = new FakeStructuredFinancialMetricsProvider(null);
        var service = new FakePythonFinancialAnalysisService();
        var workflow = CreateWorkflow(
            provider,
            service,
            publisher,
            new DataAgentOptions
            {
                RequireSessionFinancialMetrics = true
            }
        );

        var result = await workflow.AnalyzeAsync(
            CreateReport(),
            CancellationToken.None
        );

        result.Engine.Should().Be("Semantic Kernel + CSnakes + Python/Pandas");
        result.HasAnomaly.Should().BeTrue();
        result.Severity.Should().Be("Medium");
        result.Summary.Should().Be("Structured financial metrics are required but were not attached to this session.");
        result.FinancialAnalysis.Should().NotBeNull();
        result.FinancialAnalysis!.MetricsInputSource.Should().Be(FinancialMetricsInputSources.None);
        result.FinancialAnalysis.Warnings.Should().Contain(
            "Structured financial metrics are required for this mode but were not attached to the session."
        );
        result.FinancialAnalysis.Limitations.Should().Contain(
            "No financial ratios or period comparisons were computed because no structured metrics were available."
        );
        result.FinancialAnalysis.AiReview.Should().NotBeNull();
        result.FinancialAnalysis.AiReview!.Summary.Should().Be("AI review was not executed.");
        result.FinancialAnalysis.AiReview.FailureReason.Should().Be("structured_financial_metrics_missing");
        service.ComputeCalls.Should().Be(0);
        publisher.PublishedEvents.Should().ContainSingle(e =>
            e.Type == "financial_metrics_required_missing" &&
            e.Agent == "DataAgent"
        );
    }

    [Fact]
    public async Task AnalyzeAsync_Should_use_metrics_returned_by_provider()
    {
        var provider = new FakeStructuredFinancialMetricsProvider(
            CreateMetricsDocument(
                documentId: "manual-structured-metrics-test",
                company: "Manual Test Co",
                currency: "ARS",
                unit: "ARS_thousand"
            )
        );
        var service = new FakePythonFinancialAnalysisService();
        var workflow = CreateWorkflow(provider, service);

        var result = await workflow.AnalyzeAsync(
            CreateReport(),
            CancellationToken.None
        );

        result.FinancialAnalysis.Should().NotBeNull();
        result.FinancialAnalysis!.DocumentId.Should().Be("manual-structured-metrics-test");
        result.FinancialAnalysis.Company.Should().Be("Manual Test Co");
        service.ComputeRequest.Should().NotBeNull();
        service.ComputeRequest!.Metrics.Should().Contain(metric =>
            metric.Name == "revenue" &&
            metric.Unit == "ARS_thousand" &&
            metric.Currency == "ARS"
        );
    }

    [Fact]
    public async Task AnalyzeAsync_Should_store_metrics_input_source_and_provenance()
    {
        var provenance = new StructuredFinancialMetricsProvenance(
            IngestionMethod: "json_file",
            OriginalFileName: "metrics.json",
            FileSizeBytes: 1024,
            ContentHash: "abc123",
            MetricCount: 4,
            WarningCount: 0
        );
        var provider = new FakeStructuredFinancialMetricsProvider(
            CreateMetricsDocument(
                inputSource: FinancialMetricsInputSources.SessionContext,
                provenance: provenance
            )
        );
        var service = new FakePythonFinancialAnalysisService();
        var workflow = CreateWorkflow(provider, service);

        var result = await workflow.AnalyzeAsync(
            CreateReport(),
            CancellationToken.None
        );

        result.FinancialAnalysis.Should().NotBeNull();
        result.FinancialAnalysis!.MetricsInputSource
            .Should()
            .Be(FinancialMetricsInputSources.SessionContext);
        result.FinancialAnalysis.MetricsProvenance.Should().BeSameAs(provenance);
    }

    [Fact]
    public async Task AnalyzeAsync_Should_store_fixture_metrics_input_source()
    {
        var publisher = new FakeActivityEventPublisher();
        var provider = new FakeStructuredFinancialMetricsProvider(
            CreateMetricsDocument(
                inputSource: FinancialMetricsInputSources.FixtureFallback
            )
        );
        var service = new FakePythonFinancialAnalysisService();
        var workflow = CreateWorkflow(provider, service, publisher);

        var result = await workflow.AnalyzeAsync(
            CreateReport(),
            CancellationToken.None
        );

        result.FinancialAnalysis.Should().NotBeNull();
        result.FinancialAnalysis!.MetricsInputSource
            .Should()
            .Be(FinancialMetricsInputSources.FixtureFallback);
        result.FinancialAnalysis.MetricsProvenance.Should().BeNull();
        result.FinancialAnalysis.Warnings.Should().Contain(
            "Fixture fallback metrics were used. This mode is intended for development/demo only."
        );
        publisher.PublishedEvents.Should().ContainSingle(e =>
            e.Type == "financial_metrics_fixture_fallback_used" &&
            e.Agent == "DataAgent"
        );
    }

    [Fact]
    public async Task AnalyzeAsync_Should_not_add_fixture_warning_or_event_for_session_metrics()
    {
        var publisher = new FakeActivityEventPublisher();
        var provider = new FakeStructuredFinancialMetricsProvider(
            CreateMetricsDocument(
                inputSource: FinancialMetricsInputSources.SessionContext
            )
        );
        var service = new FakePythonFinancialAnalysisService();
        var workflow = CreateWorkflow(provider, service, publisher);

        var result = await workflow.AnalyzeAsync(
            CreateReport(),
            CancellationToken.None
        );

        result.FinancialAnalysis.Should().NotBeNull();
        result.FinancialAnalysis!.MetricsInputSource
            .Should()
            .Be(FinancialMetricsInputSources.SessionContext);
        result.FinancialAnalysis.Warnings.Should().NotContain(
            "Fixture fallback metrics were used. This mode is intended for development/demo only."
        );
        publisher.PublishedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task AnalyzeAsync_Should_run_financial_analysis_when_session_metrics_exist_and_required()
    {
        var provider = new FakeStructuredFinancialMetricsProvider(
            CreateMetricsDocument(
                inputSource: FinancialMetricsInputSources.SessionContext
            )
        );
        var service = new FakePythonFinancialAnalysisService();
        var workflow = CreateWorkflow(
            provider,
            service,
            options: new DataAgentOptions
            {
                RequireSessionFinancialMetrics = true
            }
        );

        var result = await workflow.AnalyzeAsync(
            CreateReport(),
            CancellationToken.None
        );

        result.FinancialAnalysis.Should().NotBeNull();
        result.FinancialAnalysis!.MetricsInputSource.Should().Be(FinancialMetricsInputSources.SessionContext);
        service.ComputeCalls.Should().Be(1);
    }

    [Fact]
    public async Task AnalyzeAsync_Should_not_modify_quantitative_outputs_when_ai_review_runs()
    {
        var provider = new FakeStructuredFinancialMetricsProvider(CreateMetricsDocument());
        var service = new FakePythonFinancialAnalysisService();
        var aiReviewService = new FakeDataAgentAiReviewService();
        var workflow = CreateWorkflow(
            provider,
            service,
            aiReviewService: aiReviewService
        );

        var result = await workflow.AnalyzeAsync(
            CreateReport(),
            CancellationToken.None
        );

        result.FinancialAnalysis.Should().NotBeNull();
        aiReviewService.LastInput.Should().NotBeNull();
        result.FinancialAnalysis!.Ratios.Should().BeEquivalentTo(aiReviewService.LastInput!.Ratios);
        result.FinancialAnalysis.Comparisons.Should().BeEquivalentTo(aiReviewService.LastInput.PeriodComparisons);
        result.FinancialAnalysis.RiskSignals.Should().BeEquivalentTo(aiReviewService.LastInput.RiskSignals);
        result.FinancialAnalysis.RiskEvidence.Should().BeEquivalentTo(aiReviewService.LastInput.RiskEvidence);
    }

    [Fact]
    public async Task AnalyzeAsync_Should_not_turn_warnings_into_anomaly_evidence_when_no_risk_signals_exist()
    {
        var provider = new FakeStructuredFinancialMetricsProvider(CreateMetricsDocument());
        var service = new FakePythonFinancialAnalysisService
        {
            ReturnNoRiskSignals = true,
            RatioWarnings = ["Missing input metric: current_liabilities."],
            SignalWarnings = ["Insufficient comparable periods for trend risk signals."]
        };
        var workflow = CreateWorkflow(provider, service);

        var result = await workflow.AnalyzeAsync(
            CreateReport(),
            CancellationToken.None
        );

        result.HasAnomaly.Should().BeFalse();
        result.Severity.Should().Be("Low");
        result.Evidence.Should().BeEmpty();
        result.Summary.Should().Contain("No quantitative risk signals were detected");
        result.FinancialAnalysis.Should().NotBeNull();
        result.FinancialAnalysis!.RiskSignals.Should().BeEmpty();
        result.FinancialAnalysis.RiskEvidence.Should().BeEmpty();
        result.FinancialAnalysis.Warnings.Should().Contain("Missing input metric: current_liabilities.");
        result.FinancialAnalysis.Warnings.Should().Contain("Insufficient comparable periods for trend risk signals.");
    }

    private static DataAgentFinancialAnalysisWorkflow CreateWorkflow(
        FakeStructuredFinancialMetricsProvider provider,
        FakePythonFinancialAnalysisService service,
        FakeActivityEventPublisher? publisher = null,
        DataAgentOptions? options = null,
        FakeDataAgentAiReviewService? aiReviewService = null,
        IFinancialRiskThresholdProfileProvider? profileProvider = null)
    {
        return new DataAgentFinancialAnalysisWorkflow(
            provider,
            service,
            aiReviewService ?? new FakeDataAgentAiReviewService(),
            profileProvider ?? new InMemoryFinancialRiskThresholdProfileProvider(),
            Options.Create(options ?? new DataAgentOptions()),
            publisher ?? new FakeActivityEventPublisher(),
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

    private static StructuredFinancialMetricsDocument CreateMetricsDocument(
        string documentId = "vista-energy-fixture",
        string company = "Vista Energy",
        string currency = "USD",
        string unit = "USD millions",
        string inputSource = FinancialMetricsInputSources.Unknown,
        StructuredFinancialMetricsProvenance? provenance = null)
    {
        return new StructuredFinancialMetricsDocument(
            DocumentId: documentId,
            Company: company,
            Currency: currency,
            Unit: unit,
            Metrics:
            [
                CreateMetric("revenue", "2024A", 100m, unit, currency),
                CreateMetric("revenue", "2025E", 80m, unit, currency),
                CreateMetric("ebitda", "2025E", 12m, unit, currency),
                CreateMetric("net_debt", "2025E", 45m, unit, currency)
            ],
            InputSource: inputSource,
            Provenance: provenance
        );
    }

    private static FinancialMetric CreateMetric(
        string name,
        string period,
        decimal value,
        string unit = "USD millions",
        string currency = "USD")
    {
        return new FinancialMetric(
            Name: name,
            Period: period,
            Value: value,
            Unit: unit,
            Statement: "unit_test",
            Source: "unit_test",
            Currency: currency,
            SourcePage: 18,
            Confidence: 0.9m
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

        public ComputeFinancialRatiosRequest? ComputeRequest { get; private set; }

        public IReadOnlyList<string> RatioWarnings { get; init; } = [];

        public IReadOnlyList<string> SignalWarnings { get; init; } = [];

        public bool ReturnNoRiskSignals { get; init; }

        public Task<ComputeFinancialRatiosResponse> ComputeFinancialRatiosAsync(
            ComputeFinancialRatiosRequest request,
            CancellationToken cancellationToken)
        {
            ComputeCalls++;
            ComputeRequest = request;

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
            if (ReturnNoRiskSignals)
            {
                return Task.FromResult(new DetectFinancialRiskSignalsResponse(
                    Engine: "Fake Financial Analysis",
                    Signals: [],
                    Result: new FinancialAnalysisToolResult(
                        HasRiskSignals: false,
                        RiskLevel: "Low",
                        Summary: "No financial risk signals detected.",
                        Engine: "Fake Financial Analysis",
                        Evidence: [],
                        Warnings: SignalWarnings
                    )
                ));
            }

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
            if (ReturnNoRiskSignals)
            {
                return Task.FromResult(new SummarizeQuantitativeEvidenceResponse(
                    Engine: "Fake Financial Analysis",
                    Narrative: "No quantitative risk signals were detected from the provided structured metrics. Review warnings and limitations for data coverage.",
                    Result: new FinancialAnalysisToolResult(
                        HasRiskSignals: false,
                        RiskLevel: "Low",
                        Summary: "No quantitative risk signals were detected.",
                        Engine: "Fake Financial Analysis",
                        Evidence: [],
                        Warnings: []
                    )
                ));
            }

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

    private sealed class FakeDataAgentAiReviewService : IDataAgentAiReviewService
    {
        public int Calls { get; private set; }

        public FinancialAnalysisAiReviewInput? LastInput { get; private set; }

        public Task<FinancialAnalysisAiReviewResult> ReviewAsync(
            FinancialAnalysisAiReviewInput input,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastInput = input;

            return Task.FromResult(new FinancialAnalysisAiReviewResult(
                Summary: "Deterministic AI review summarized financial evidence.",
                KeyFindings:
                [
                    new FinancialAnalysisAiKeyFinding(
                        Title: "Leverage watch",
                        Description: "Leverage should be reviewed.",
                        Severity: "High",
                        RelatedMetrics: ["net_debt_to_ebitda"]
                    )
                ],
                RiskInterpretation: "Human review should focus on deterministic risk evidence.",
                DataQualityNotes:
                [
                    new FinancialAnalysisAiDataQualityNote(
                        Message: "Structured metrics were used.",
                        Severity: "Info",
                        RelatedFields: ["metricsInputSource"]
                    )
                ],
                Limitations: ["AI review is advisory."],
                UsedLlm: false,
                UsedFallback: true,
                Provider: null,
                Model: null,
                FailureReason: null
            ));
        }
    }
}
