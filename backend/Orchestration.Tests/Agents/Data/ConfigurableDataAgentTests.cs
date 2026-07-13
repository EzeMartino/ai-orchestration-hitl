using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Shared;
using Orchestration.Infrastructure.Agents.Data;

namespace Orchestration.Tests.Agents.Data;

public sealed class ConfigurableDataAgentTests
{
    [Fact]
    public async Task AnalyzeAsync_Should_use_legacy_path_when_financial_analysis_is_disabled()
    {
        var legacy = new FakeLegacyDataAgent();
        var workflow = new FakeFinancialAnalysisWorkflow();
        var agent = CreateAgent(
            legacy,
            workflow,
            new DataAgentOptions
            {
                FinancialAnalysisToolsEnabled = false
            }
        );

        var result = await agent.AnalyzeAsync(
            CreateReport(),
            CancellationToken.None
        );

        legacy.WasCalled.Should().BeTrue();
        workflow.WasCalled.Should().BeFalse();
        result.Engine.Should().Be("Legacy DataAgent");
    }

    [Fact]
    public async Task AnalyzeAsync_Should_use_financial_workflow_when_enabled()
    {
        var legacy = new FakeLegacyDataAgent();
        var workflow = new FakeFinancialAnalysisWorkflow();
        var agent = CreateAgent(
            legacy,
            workflow,
            new DataAgentOptions
            {
                FinancialAnalysisToolsEnabled = true
            }
        );

        var result = await agent.AnalyzeAsync(
            CreateReport(),
            CancellationToken.None
        );

        legacy.WasCalled.Should().BeFalse();
        workflow.WasCalled.Should().BeTrue();
        result.Engine.Should().Be("Financial Workflow");
    }

    [Fact]
    public async Task AnalyzeAsync_Should_fallback_to_legacy_when_financial_workflow_fails_and_fallback_is_enabled()
    {
        var legacy = new FakeLegacyDataAgent
        {
            Result = new DataAgentResult(
                HasAnomaly: false,
                Severity: "Low",
                Summary: "Legacy analysis completed without anomaly.",
                Engine: "Legacy DataAgent",
                Evidence: [])
        };
        var workflow = new FakeFinancialAnalysisWorkflow
        {
            ThrowOnAnalyze = true
        };
        var agent = CreateAgent(
            legacy,
            workflow,
            new DataAgentOptions
            {
                FinancialAnalysisToolsEnabled = true,
                UseLegacyAnomalyDetectionFallback = true
            }
        );

        var result = await agent.AnalyzeAsync(
            CreateReport(),
            CancellationToken.None
        );

        legacy.WasCalled.Should().BeTrue();
        workflow.WasCalled.Should().BeTrue();
        result.Engine.Should().Be("Legacy DataAgent (respaldo legacy)");
        result.HasAnomaly.Should().BeFalse();
        result.Severity.Should().Be("Low");
        result.Evidence.Should().BeEmpty();
        result.RequiresHumanReview.Should().BeTrue();
        result.Summary.Should().StartWith("Legacy analysis completed without anomaly.");
        result.Summary.Should().Contain("No se pudo completar el análisis financiero estructurado");
        result.Summary.Should().Contain("requiere revisión humana");
    }

    [Fact]
    public async Task AnalyzeAsync_Should_return_safe_result_when_financial_workflow_fails_and_fallback_is_disabled()
    {
        var legacy = new FakeLegacyDataAgent();
        var workflow = new FakeFinancialAnalysisWorkflow
        {
            ThrowOnAnalyze = true
        };
        var agent = CreateAgent(
            legacy,
            workflow,
            new DataAgentOptions
            {
                FinancialAnalysisToolsEnabled = true,
                UseLegacyAnomalyDetectionFallback = false
            }
        );

        var result = await agent.AnalyzeAsync(
            CreateReport(),
            CancellationToken.None
        );

        legacy.WasCalled.Should().BeFalse();
        workflow.WasCalled.Should().BeTrue();
        result.Engine.Should().Be("Financial Analysis Workflow");
        result.HasAnomaly.Should().BeFalse();
        result.Severity.Should().Be("Unknown");
        result.RequiresHumanReview.Should().BeTrue();
        result.Summary.Should().Contain("No se pudo completar el análisis financiero estructurado");
        result.Summary.Should().Contain("requiere revisión humana");
    }

    [Fact]
    public async Task AnalyzeAsync_Should_log_safe_structured_failure_metadata()
    {
        var legacy = new FakeLegacyDataAgent();
        var workflow = new FakeFinancialAnalysisWorkflow { ThrowOnAnalyze = true };
        var logger = new TestCapturingLogger<ConfigurableDataAgent>();
        var agent = CreateAgent(
            legacy,
            workflow,
            new DataAgentOptions { FinancialAnalysisToolsEnabled = true },
            logger);
        var report = CreateReport();

        await agent.AnalyzeAsync(report, CancellationToken.None);

        var failureLog = logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Warning &&
            entry.State.ContainsKey("FailureCode")).Subject;
        failureLog.State["SessionId"].Should().Be(report.SessionId);
        failureLog.State["FailureCode"].Should().Be("FINANCIAL_ANALYSIS_UNEXPECTED_FAILURE");
        failureLog.Message.Should().NotContain("Financial workflow failed.");
    }

    [Fact]
    public async Task AnalyzeAsync_Should_not_use_legacy_fallback_for_missing_required_metrics_result()
    {
        var legacy = new FakeLegacyDataAgent();
        var workflow = new FakeFinancialAnalysisWorkflow
        {
            Result = new DataAgentResult(
                HasAnomaly: true,
                Severity: "Medium",
                Summary: "Structured financial metrics are required but were not attached to this session.",
                Engine: "Financial Workflow",
                Evidence: [],
                FinancialAnalysis: new FinancialAnalysisContext(
                    Engine: "Financial Workflow",
                    DocumentId: "missing-required-metrics",
                    Company: null,
                    Ratios: [],
                    Comparisons: [],
                    RiskSignals: [],
                    RiskEvidence: [],
                    Warnings:
                    [
                        "Structured financial metrics are required for this mode but were not attached to the session."
                    ],
                    Limitations: [],
                    MetricsInputSource: FinancialMetricsInputSources.None
                )
            )
        };
        var agent = CreateAgent(
            legacy,
            workflow,
            new DataAgentOptions
            {
                FinancialAnalysisToolsEnabled = true,
                RequireSessionFinancialMetrics = true,
                UseLegacyAnomalyDetectionFallback = true
            }
        );

        var result = await agent.AnalyzeAsync(
            CreateReport(),
            CancellationToken.None
        );

        legacy.WasCalled.Should().BeFalse();
        workflow.WasCalled.Should().BeTrue();
        result.Summary.Should().Be("Structured financial metrics are required but were not attached to this session.");
        result.FinancialAnalysis!.MetricsInputSource.Should().Be(FinancialMetricsInputSources.None);
    }

    private static ConfigurableDataAgent CreateAgent(
        FakeLegacyDataAgent legacyDataAgent,
        FakeFinancialAnalysisWorkflow financialAnalysisWorkflow,
        DataAgentOptions options,
        ILogger<ConfigurableDataAgent>? logger = null)
    {
        return new ConfigurableDataAgent(
            legacyDataAgent,
            financialAnalysisWorkflow,
            Options.Create(options),
            logger ?? NullLogger<ConfigurableDataAgent>.Instance
        );
    }

    private static FinancialReportContext CreateReport()
    {
        return new FinancialReportContext(
            SessionId: Guid.NewGuid(),
            ReportName: "configurable-agent-test-report",
            TotalAmount: 125000m,
            TransactionCount: 42,
            SubmittedAt: DateTimeOffset.UtcNow
        );
    }

    private sealed class FakeLegacyDataAgent : ILegacyDataAgent
    {
        public bool WasCalled { get; private set; }

        public DataAgentResult? Result { get; init; }

        public Task<DataAgentResult> AnalyzeAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            WasCalled = true;

            return Task.FromResult(Result ?? new DataAgentResult(
                HasAnomaly: true,
                Severity: "High",
                Summary: "Legacy anomaly detection completed.",
                Engine: "Legacy DataAgent",
                Evidence:
                [
                    new AnomalyEvidence(
                        Metric: "TransactionAmountZScore",
                        Value: 4.5,
                        Threshold: 3.0,
                        Interpretation: "Above threshold."
                    )
                ]
            ));
        }
    }

    private sealed class FakeFinancialAnalysisWorkflow : IDataAgentFinancialAnalysisWorkflow
    {
        public bool WasCalled { get; private set; }

        public bool ThrowOnAnalyze { get; init; }

        public DataAgentResult? Result { get; init; }

        public Task<DataAgentResult> AnalyzeAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            WasCalled = true;

            if (ThrowOnAnalyze)
            {
                throw new InvalidOperationException("Financial workflow failed.");
            }

            return Task.FromResult(Result ?? new DataAgentResult(
                HasAnomaly: true,
                Severity: "Medium",
                Summary: "Financial workflow completed.",
                Engine: "Financial Workflow",
                Evidence:
                [
                    new AnomalyEvidence(
                        Metric: "current_ratio",
                        Value: 0.8,
                        Threshold: 1.0,
                        Interpretation: "Liquidity should be reviewed."
                    )
                ]
            ));
        }
    }
}
