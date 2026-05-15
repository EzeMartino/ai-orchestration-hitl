using FluentAssertions;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Planner;
using Orchestration.Application.Agents.Planner.Reasoning;
using Orchestration.Application.Agents.Planner.ToolCalling;
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
            reasoningService,
            new FakeToolPlanProposalService(),
            new ToolPlanNormalizer(),
            new ToolPlanValidator()
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
    public async Task RunAsync_Should_carry_empty_tool_plan_audit_result_when_tool_calling_is_disabled()
    {
        var plannerAgent = new PlannerAgent(
            new FakeDataAgent(CreateDataResult(hasAnomaly: false)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false)),
            new FakeActivityEventPublisher(),
            new FakePlannerReasoningService(),
            new DeterministicToolPlanProposalService(
                new ToolCallingOptions
                {
                    Enabled = false
                }
            ),
            new ToolPlanNormalizer(),
            new ToolPlanValidator()
        );

        var result = await plannerAgent.RunAsync(
            AnalysisSession.Create(),
            CancellationToken.None
        );

        result.ToolPlan.ProposedCalls.Should().BeEmpty();
        result.ToolPlan.ApprovedCalls.Should().BeEmpty();
        result.ToolPlan.RejectedCalls.Should().BeEmpty();
        result.ToolPlan.ExecutedCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_Should_not_publish_tool_plan_events_when_tool_plan_is_empty()
    {
        var publisher = new FakeActivityEventPublisher();
        var plannerAgent = new PlannerAgent(
            new FakeDataAgent(CreateDataResult(hasAnomaly: false)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false)),
            publisher,
            new FakePlannerReasoningService(),
            new FakeToolPlanProposalService(),
            new ToolPlanNormalizer(),
            new ToolPlanValidator()
        );

        await plannerAgent.RunAsync(
            AnalysisSession.Create(),
            CancellationToken.None
        );

        publisher.PublishedEvents.Should().NotContain(x =>
            x.Type == "tool_plan_proposed" ||
            x.Type == "tool_plan_validated" ||
            x.Type == "tool_call_rejected" ||
            x.Type == "tool_call_executed"
        );
    }

    [Fact]
    public async Task RunAsync_Should_publish_planner_reasoning_completed_event()
    {
        var publisher = new FakeActivityEventPublisher();
        var plannerAgent = new PlannerAgent(
            new FakeDataAgent(CreateDataResult(hasAnomaly: false)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false)),
            publisher,
            new FakePlannerReasoningService(),
            new FakeToolPlanProposalService(),
            new ToolPlanNormalizer(),
            new ToolPlanValidator()
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
            new FakePlannerReasoningService(reasoningResult),
            new FakeToolPlanProposalService(),
            new ToolPlanNormalizer(),
            new ToolPlanValidator()
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
            new FakePlannerReasoningService(),
            new FakeToolPlanProposalService(),
            new ToolPlanNormalizer(),
            new ToolPlanValidator()
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
            new FakePlannerReasoningService(),
            new FakeToolPlanProposalService(),
            new ToolPlanNormalizer(),
            new ToolPlanValidator()
        );

        var result = await plannerAgent.RunAsync(
            AnalysisSession.Create(),
            CancellationToken.None
        );

        result.RequiresHumanApproval.Should().BeTrue();
        result.DataResult.HasAnomaly.Should().BeFalse();
        result.LegalResult.HasComplianceRisk.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_Should_include_proposed_and_approved_tool_plan_when_tool_calling_is_enabled()
    {
        var plannerAgent = new PlannerAgent(
            new FakeDataAgent(CreateDataResult(hasAnomaly: false)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false)),
            new FakeActivityEventPublisher(),
            new FakePlannerReasoningService(),
            new DeterministicToolPlanProposalService(
                new ToolCallingOptions
                {
                    Enabled = true
                }
            ),
            new ToolPlanNormalizer(),
            new ToolPlanValidator()
        );

        var result = await plannerAgent.RunAsync(
            AnalysisSession.Create(),
            CancellationToken.None
        );

        result.ToolPlan.ProposedCalls.Should().HaveCount(2);
        result.ToolPlan.ApprovedCalls.Should().HaveCount(2);
        result.ToolPlan.RejectedCalls.Should().BeEmpty();
        result.ToolPlan.ExecutedCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_Should_publish_tool_plan_events_when_proposed_calls_exist()
    {
        var publisher = new FakeActivityEventPublisher();
        var plannerAgent = new PlannerAgent(
            new FakeDataAgent(CreateDataResult(hasAnomaly: false)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false)),
            publisher,
            new FakePlannerReasoningService(),
            new DeterministicToolPlanProposalService(
                new ToolCallingOptions
                {
                    Enabled = true
                }
            ),
            new ToolPlanNormalizer(),
            new ToolPlanValidator()
        );

        await plannerAgent.RunAsync(
            AnalysisSession.Create(),
            CancellationToken.None
        );

        publisher.PublishedEvents.Should().Contain(x =>
            x.Type == "tool_plan_proposed" &&
            x.Message == "PlannerAgent proposed 2 tool calls."
        );
        publisher.PublishedEvents.Should().Contain(x =>
            x.Type == "tool_plan_validated" &&
            x.Message == "Tool plan validated: 2 approved, 0 rejected."
        );
        publisher.PublishedEvents.Should().NotContain(x => x.Type == "tool_call_executed");
    }

    [Fact]
    public async Task RunAsync_Should_publish_tool_call_rejected_when_validator_rejects_call()
    {
        var publisher = new FakeActivityEventPublisher();
        var proposedPlan = new ToolPlan(
            [
                CreateProposedToolCall("workflow.complete")
            ]
        );

        var plannerAgent = new PlannerAgent(
            new FakeDataAgent(CreateDataResult(hasAnomaly: false)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false)),
            publisher,
            new FakePlannerReasoningService(),
            new FakeToolPlanProposalService(proposedPlan),
            new ToolPlanNormalizer(),
            new ToolPlanValidator()
        );

        var result = await plannerAgent.RunAsync(
            AnalysisSession.Create(),
            CancellationToken.None
        );

        result.ToolPlan.ExecutedCalls.Should().BeEmpty();
        result.ToolPlan.RejectedCalls.Should().ContainSingle().Which.Should().Be(
            new RejectedToolCall(
                "workflow.complete",
                "Workflow transition tools are not allowed."
            )
        );
        publisher.PublishedEvents.Should().Contain(x =>
            x.Type == "tool_call_rejected" &&
            x.Message == "Rejected tool call 'workflow.complete': Workflow transition tools are not allowed."
        );
        publisher.PublishedEvents.Should().NotContain(x => x.Type == "tool_call_executed");
    }

    [Fact]
    public async Task RunAsync_Should_validate_normalized_tool_plan()
    {
        var proposedPlan = new ToolPlan(
            [
                CreateProposedToolCall(
                    " data.analyze_transactions ",
                    new Dictionary<string, string>
                    {
                        [" sessionId "] = "session-1"
                    },
                    " Analyze data. "
                ),
                CreateProposedToolCall(
                    "data.analyze_transactions",
                    new Dictionary<string, string>
                    {
                        ["sessionId"] = "session-1"
                    },
                    "Duplicate data call."
                ),
                CreateProposedToolCall(
                    "legal.search_cnv_regulation",
                    new Dictionary<string, string>
                    {
                        ["query"] = "agentes",
                        ["area"] = "Agentes"
                    },
                    "Retrieve legal evidence."
                )
            ]
        );

        var plannerAgent = new PlannerAgent(
            new FakeDataAgent(CreateDataResult(hasAnomaly: false)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false)),
            new FakeActivityEventPublisher(),
            new FakePlannerReasoningService(),
            new FakeToolPlanProposalService(proposedPlan),
            new ToolPlanNormalizer(),
            new ToolPlanValidator()
        );

        var result = await plannerAgent.RunAsync(
            AnalysisSession.Create(),
            CancellationToken.None
        );

        result.ToolPlan.ProposedCalls.Should().HaveCount(2);
        result.ToolPlan.ApprovedCalls.Should().HaveCount(2);
        result.ToolPlan.RejectedCalls.Should().BeEmpty();
        result.ToolPlan.ExecutedCalls.Should().BeEmpty();
        result.ToolPlan.ProposedCalls[0].ToolName.Should().Be("data.analyze_transactions");
        result.ToolPlan.ProposedCalls[0].Reason.Should().Be("Analyze data.");
        result.ToolPlan.ApprovedCalls[0].ToolName.Should().Be("data.analyze_transactions");
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

    private static ProposedToolCall CreateProposedToolCall(
        string toolName,
        IReadOnlyDictionary<string, string>? arguments = null,
        string reason = "Planner proposed read-only evidence collection.")
    {
        return new ProposedToolCall(
            ToolName: toolName,
            Arguments: arguments ?? new Dictionary<string, string>
            {
                ["sessionId"] = Guid.NewGuid().ToString()
            },
            Reason: reason
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

    private sealed class FakeToolPlanProposalService : IToolPlanProposalService
    {
        private readonly ToolPlan _result;

        public FakeToolPlanProposalService(
            ToolPlan? result = null)
        {
            _result = result ?? new ToolPlan([]);
        }

        public Task<ToolPlan> ProposeAsync(
            ToolPlanProposalInput input,
            CancellationToken cancellationToken)
        {
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
