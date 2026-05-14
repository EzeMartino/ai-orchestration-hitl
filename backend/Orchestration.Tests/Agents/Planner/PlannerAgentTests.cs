using FluentAssertions;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Planner;
using Orchestration.Application.Agents.Planner.Reasoning;
using Orchestration.Application.Agents.Shared;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Tests.Agents;

namespace Orchestration.Tests.Agents.Planner;

public class PlannerAgentTests
{
    [Fact]
    public async Task RunAsync_Should_call_reasoning_service()
    {
        var reasoningService = new FakePlannerReasoningService();
        var plannerAgent = new PlannerAgent(
            new FakeDataAgent(CreateDataResult(hasAnomaly: false)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false)),
            new FakeActivityEventPublisher(),
            reasoningService
        );

        var result = await plannerAgent.RunAsync(
            AnalysisSession.Create(),
            CancellationToken.None
        );

        reasoningService.WasCalled.Should().BeTrue();
        reasoningService.Input.Should().NotBeNull();
        result.ReasoningResult.Engine.Should().Be("Test Planner Reasoning");
    }

    [Fact]
    public async Task RunAsync_Should_publish_planner_reasoning_completed_event()
    {
        var publisher = new FakeActivityEventPublisher();
        var plannerAgent = new PlannerAgent(
            new FakeDataAgent(CreateDataResult(hasAnomaly: false)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false)),
            publisher,
            new FakePlannerReasoningService()
        );

        await plannerAgent.RunAsync(
            AnalysisSession.Create(),
            CancellationToken.None
        );

        publisher.PublishedEvents
            .Should()
            .Contain(x =>
                x.Type == "planner_reasoning_completed" &&
                x.Agent == "PlannerAgent" &&
                x.Message == "Planner reasoning completed using deterministic fallback.");
    }

    [Fact]
    public async Task RunAsync_Should_publish_fallback_event_when_llm_reasoning_falls_back_after_failure()
    {
        var publisher = new FakeActivityEventPublisher();
        var reasoningResult = CreatePlannerReasoningResult() with
        {
            Provider = "OpenAI",
            Model = "test-model",
            FailureReason = "LLM returned invalid JSON."
        };

        var plannerAgent = new PlannerAgent(
            new FakeDataAgent(CreateDataResult(hasAnomaly: false)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false)),
            publisher,
            new FakePlannerReasoningService(reasoningResult)
        );

        await plannerAgent.RunAsync(
            AnalysisSession.Create(),
            CancellationToken.None
        );

        publisher.PublishedEvents
            .Should()
            .Contain(x =>
                x.Type == "planner_reasoning_fallback_used" &&
                x.Agent == "PlannerAgent" &&
                x.Message == "LLM reasoning failed; deterministic fallback was used.");
    }

    [Fact]
    public async Task RunAsync_Should_require_human_approval_when_data_agent_detects_anomaly()
    {
        var dataAgent = new FakeDataAgent(CreateDataResult(hasAnomaly: true));
        var legalAgent = new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false));
        var publisher = new FakeActivityEventPublisher();

        var plannerAgent = new PlannerAgent(
            dataAgent,
            legalAgent,
            publisher,
            new FakePlannerReasoningService()
        );

        var result = await plannerAgent.RunAsync(
            AnalysisSession.Create(),
            CancellationToken.None
        );

        result.RequiresHumanApproval.Should().BeTrue();
        result.DataResult.HasAnomaly.Should().BeTrue();
        result.LegalResult.HasComplianceRisk.Should().BeFalse();

        dataAgent.WasCalled.Should().BeTrue();
        legalAgent.WasCalled.Should().BeTrue();

        publisher.PublishedEvents
            .Should()
            .Contain(x => x.Type == "tool_executed" && x.Agent == "DataAgent");

        publisher.PublishedEvents
            .Should()
            .Contain(x => x.Agent == "LegalAgent");
    }

    [Fact]
    public async Task RunAsync_Should_require_human_approval_when_legal_agent_detects_compliance_risk()
    {
        var plannerAgent = new PlannerAgent(
            new FakeDataAgent(CreateDataResult(hasAnomaly: false)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: true)),
            new FakeActivityEventPublisher(),
            new FakePlannerReasoningService()
        );

        var result = await plannerAgent.RunAsync(
            AnalysisSession.Create(),
            CancellationToken.None
        );

        result.RequiresHumanApproval.Should().BeTrue();
        result.DataResult.HasAnomaly.Should().BeFalse();
        result.LegalResult.HasComplianceRisk.Should().BeTrue();
    }

    private static DataAgentResult CreateDataResult(bool hasAnomaly)
    {
        return new DataAgentResult(
            HasAnomaly: hasAnomaly,
            Severity: hasAnomaly ? "High" : "Low",
            Summary: hasAnomaly ? "Anomaly detected." : "No anomaly detected.",
            Engine: "TestEngine",
            Evidence: hasAnomaly
                ? [
                    new AnomalyEvidence(
                        Metric: "TransactionAmountZScore",
                        Value: 4.5,
                        Threshold: 3.0,
                        Interpretation: "Above threshold."
                    )
                ]
                : []
        );
    }

    private static LegalAgentResult CreateLegalResult(bool hasComplianceRisk)
    {
        return new LegalAgentResult(
            HasComplianceRisk: hasComplianceRisk,
            RiskLevel: hasComplianceRisk ? "Medium" : "Low",
            Summary: hasComplianceRisk ? "Compliance review required." : "No compliance risk.",
            Engine: "TestEngine",
            Evidence: hasComplianceRisk
                ? [
                    new LegalEvidence(
                        Regulation: "CNV",
                        Section: "Transaction Monitoring",
                        Finding: "Human review required.",
                        Source: "Test source"
                    )
                ]
                : [],
            Warnings: hasComplianceRisk
                ? ["Automated regulatory retrieval only."]
                : []
        );
    }

    private sealed class FakeDataAgent : IDataAgent
    {
        private readonly DataAgentResult _result;

        public bool WasCalled { get; private set; }

        public FakeDataAgent(DataAgentResult result)
        {
            _result = result;
        }

        public Task<DataAgentResult> AnalyzeAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            WasCalled = true;

            return Task.FromResult(_result);
        }
    }

    private sealed class FakeLegalAgent : ILegalAgent
    {
        private readonly LegalAgentResult _result;

        public bool WasCalled { get; private set; }

        public FakeLegalAgent(LegalAgentResult result)
        {
            _result = result;
        }

        public Task<LegalAgentResult> ReviewAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            WasCalled = true;

            return Task.FromResult(_result);
        }
    }

    private sealed class FakePlannerReasoningService : IPlannerReasoningService
    {
        private readonly PlannerReasoningResult _result;

        public FakePlannerReasoningService(
            PlannerReasoningResult? result = null)
        {
            _result = result ?? CreatePlannerReasoningResult();
        }

        public bool WasCalled { get; private set; }

        public PlannerReasoningInput? Input { get; private set; }

        public Task<PlannerReasoningResult> GenerateReasoningAsync(
            PlannerReasoningInput input,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            Input = input;

            return Task.FromResult(_result);
        }
    }

    private static PlannerReasoningResult CreatePlannerReasoningResult()
    {
        return new PlannerReasoningResult(
            Engine: "Test Planner Reasoning",
            Summary: "Planner reviewed collected evidence.",
            RecommendedActions: ["Review evidence."],
            RiskFactors: ["Risk factor."],
            Limitations: ["Human approval required."],
            UsedLlm: false,
            UsedFallback: true,
            Provider: null,
            Model: null,
            FailureReason: null
        );
    }
}
