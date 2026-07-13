using System.Text.Json;
using FluentAssertions;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Planner;
using Orchestration.Application.Agents.Planner.Reasoning;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.Agents.Planner.ToolCalling.Mapping;
using Orchestration.Application.Agents.Shared;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;
using Orchestration.Infrastructure.Agents.Planner.ToolCalling;
using Orchestration.Tests.Agents;

namespace Orchestration.Tests.Agents.Planner;

public class PlannerAgentTests
{
    [Fact]
    public async Task RunAsync_PersistedReport_ShouldPropagateExactValuesToEveryConsumer()
    {
        var financialAnalysis = CreateFinancialAnalysisContext();
        var dataAgent = new FakeDataAgent(CreateDataResult(hasAnomaly: true) with
        {
            FinancialAnalysis = financialAnalysis
        });
        var legalAgent = new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false));
        var reasoningService = new FakePlannerReasoningService();
        var proposalService = new FakeToolPlanProposalService();
        var plannerAgent = new PlannerAgent(
            dataAgent,
            legalAgent,
            new FakeActivityEventPublisher(),
            reasoningService,
            proposalService,
            new ToolPlanNormalizer(),
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            new FakeControlledToolExecutor(),
            new FakeToolExecutionResultMapper(),
            new ToolCallingOptions());
        var report = TestFinancialReport.CreateContext();

        await plannerAgent.RunAsync(report, CancellationToken.None);

        dataAgent.ReceivedReport.Should().BeSameAs(report);
        legalAgent.ReceivedReport.Should().Be(report with
        {
            FinancialAnalysis = financialAnalysis
        });
        reasoningService.Input.Should().BeEquivalentTo(new
        {
            report.SessionId,
            report.ReportName,
            report.TotalAmount,
            report.TransactionCount,
            report.SubmittedAt
        });
        proposalService.Input.Should().BeEquivalentTo(new
        {
            report.SessionId,
            report.ReportName,
            report.TotalAmount,
            report.TransactionCount
        });
        legalAgent.ReceivedReport!.SubmittedAt.Should().Be(report.SubmittedAt);
    }

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
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            new FakeControlledToolExecutor(),
            new FakeToolExecutionResultMapper(),
            new ToolCallingOptions()
        );

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
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
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            new FakeControlledToolExecutor(),
            new FakeToolExecutionResultMapper(),
            new ToolCallingOptions()
        );

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
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
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            new FakeControlledToolExecutor(),
            new FakeToolExecutionResultMapper(),
            new ToolCallingOptions()
        );

        await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None
        );

        publisher.PublishedEvents.Should().NotContain(x =>
            x.Type == "tool_plan_proposed" ||
            x.Type == "tool_plan_validated" ||
            x.Type == "tool_call_rejected" ||
            x.Type == "tool_call_skipped" ||
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
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            new FakeControlledToolExecutor(),
            new FakeToolExecutionResultMapper(),
            new ToolCallingOptions()
        );

        await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None
        );

        publisher.PublishedEvents
            .Should()
            .Contain(x =>
                x.Type == "planner_reasoning_completed" &&
                x.Agent == "PlannerAgent" &&
                x.Message == "Razonamiento del Planificador completado usando la alternativa determinista.");
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
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            new FakeControlledToolExecutor(),
            new FakeToolExecutionResultMapper(),
            new ToolCallingOptions()
        );

        await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None
        );

        publisher.PublishedEvents
            .Should()
            .Contain(x =>
                x.Type == "planner_reasoning_fallback_used" &&
                x.Agent == "PlannerAgent" &&
                x.Message == "El razonamiento del LLM falló; se utilizó la alternativa determinista de contingencia.");
    }

    [Fact]
    public async Task RunAsync_Should_pass_financial_analysis_context_to_legal_agent()
    {
        var financialAnalysis = new FinancialAnalysisContext(
            Engine: "Test financial engine",
            DocumentId: "financial-doc",
            Company: "Financial Co",
            Ratios: [],
            Comparisons: [],
            RiskSignals:
            [
                new FinancialRiskSignal(
                    Name: "liquidity_risk",
                    Severity: "High",
                    Period: "2025E",
                    Summary: "Low current ratio",
                    Evidence: []
                )
            ],
            RiskEvidence: [],
            Warnings: [],
            Limitations: [],
            MetricsInputSource: FinancialMetricsInputSources.SessionContext
        );
        var legalAgent = new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false));
        var dataResult = CreateDataResult(hasAnomaly: true) with
        {
            FinancialAnalysis = financialAnalysis
        };
        var plannerAgent = new PlannerAgent(
            new FakeDataAgent(dataResult),
            legalAgent,
            new FakeActivityEventPublisher(),
            new FakePlannerReasoningService(),
            new FakeToolPlanProposalService(),
            new ToolPlanNormalizer(),
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            new FakeControlledToolExecutor(),
            new FakeToolExecutionResultMapper(),
            new ToolCallingOptions()
        );

        await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None
        );

        legalAgent.ReceivedReport.Should().NotBeNull();
        legalAgent.ReceivedReport!.FinancialAnalysis.Should().BeSameAs(financialAnalysis);
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
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            new FakeControlledToolExecutor(),
            new FakeToolExecutionResultMapper(),
            new ToolCallingOptions()
        );

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
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
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            new FakeControlledToolExecutor(),
            new FakeToolExecutionResultMapper(),
            new ToolCallingOptions()
        );

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
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
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            new FakeControlledToolExecutor(),
            new FakeToolExecutionResultMapper(),
            new ToolCallingOptions()
        );

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None
        );

        result.ToolPlan.ProposedCalls.Should().HaveCount(2);
        result.ToolPlan.ApprovedCalls.Should().HaveCount(2);
        result.ToolPlan.RejectedCalls.Should().BeEmpty();
        result.ToolPlan.ExecutedCalls.Should().HaveCount(2);
        result.ToolPlan.ExecutedCalls
            .Should()
            .OnlyContain(call => call.Status == ToolExecutionStatus.SkippedAlreadySatisfied);
        result.ToolPlan.ExecutedCalls.Should().Contain(call =>
            call.ToolName == "data.analyze_transactions" &&
            call.Summary == "DataAgent already executed during the deterministic workflow."
        );
        result.ToolPlan.ExecutedCalls.Should().Contain(call =>
            call.ToolName == "legal.search_cnv_regulation" &&
            call.Summary == "LegalAgent already executed during the deterministic workflow."
        );
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
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            new FakeControlledToolExecutor(),
            new FakeToolExecutionResultMapper(),
            new ToolCallingOptions()
        );

        await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None
        );

        publisher.PublishedEvents.Should().Contain(x =>
            x.Type == "tool_plan_proposed" &&
            x.Message == "PlannerAgent propuso 2 llamadas a herramientas."
        );
        publisher.PublishedEvents.Should().Contain(x =>
            x.Type == "tool_plan_validated" &&
            x.Message == "Plan de herramientas validado: 2 aprobadas, 0 rechazadas."
        );
        publisher.PublishedEvents.Should().Contain(x =>
            x.Type == "tool_call_skipped" &&
            x.Message == "Llamada a herramienta aprobada 'data.analyze_transactions' omitida: DataAgent already executed during the deterministic workflow."
        );
        publisher.PublishedEvents.Should().Contain(x =>
            x.Type == "tool_call_skipped" &&
            x.Message == "Llamada a herramienta aprobada 'legal.search_cnv_regulation' omitida: LegalAgent already executed during the deterministic workflow."
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
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            new FakeControlledToolExecutor(),
            new FakeToolExecutionResultMapper(),
            new ToolCallingOptions()
        );

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None
        );

        result.ToolPlan.ExecutedCalls.Should().BeEmpty();
        result.ToolPlan.RejectedCalls.Should().ContainSingle().Which.Should().Be(
            new RejectedToolCall(
                "workflow.complete",
                "No se permiten herramientas de transición de workflow."
            )
        );
        publisher.PublishedEvents.Should().Contain(x =>
            x.Type == "tool_call_rejected" &&
            x.Message == "Llamada a herramienta 'workflow.complete' rechazada: No se permiten herramientas de transición de workflow."
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
                        [" sessionId "] = Guid.Empty.ToString(),
                        [" reportName "] = "normalized-report",
                        [" totalAmount "] = "125000",
                        [" transactionCount "] = "42",
                        [" submittedAt "] = DateTimeOffset.UnixEpoch.ToString("O")
                    },
                    " Analyze data. "
                ),
                CreateProposedToolCall(
                    "data.analyze_transactions",
                    new Dictionary<string, string>
                    {
                        ["sessionId"] = Guid.Empty.ToString(),
                        ["reportName"] = "normalized-report",
                        ["totalAmount"] = "125000",
                        ["transactionCount"] = "42",
                        ["submittedAt"] = DateTimeOffset.UnixEpoch.ToString("O")
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
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            new FakeControlledToolExecutor(),
            new FakeToolExecutionResultMapper(),
            new ToolCallingOptions()
        );

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None
        );

        result.ToolPlan.ProposedCalls.Should().HaveCount(2);
        result.ToolPlan.ApprovedCalls.Should().HaveCount(2);
        result.ToolPlan.RejectedCalls.Should().BeEmpty();
        result.ToolPlan.ExecutedCalls.Should().HaveCount(2);
        result.ToolPlan.ExecutedCalls
            .Should()
            .OnlyContain(call => call.Status == ToolExecutionStatus.SkippedAlreadySatisfied);
        result.ToolPlan.ProposedCalls[0].ToolName.Should().Be("data.analyze_transactions");
        result.ToolPlan.ProposedCalls[0].Reason.Should().Be("Analyze data.");
        result.ToolPlan.ApprovedCalls[0].ToolName.Should().Be("data.analyze_transactions");
    }

    [Fact]
    public async Task RunAsync_Should_execute_approved_calls_in_plan_driven_mode()
    {
        var dataAgent = new FakeDataAgent(CreateDataResult(hasAnomaly: false));
        var legalAgent = new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false));
        var executor = new FakeControlledToolExecutor();
        var publisher = new FakeActivityEventPublisher();
        var mappedDataResult = CreateDataResult(hasAnomaly: true);
        var mappedLegalResult = CreateLegalResult(hasComplianceRisk: false);

        var plannerAgent = new PlannerAgent(
            dataAgent,
            legalAgent,
            publisher,
            new FakePlannerReasoningService(),
            new FakeToolPlanProposalService(CreateExecutableToolPlan()),
            new ToolPlanNormalizer(),
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            executor,
            new FakeToolExecutionResultMapper(mappedDataResult, mappedLegalResult),
            CreatePlanDrivenOptions()
        );

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None
        );

        executor.WasCalled.Should().BeTrue();
        executor.ReceivedCalls.Should().HaveCount(2);
        dataAgent.WasCalled.Should().BeFalse();
        legalAgent.WasCalled.Should().BeFalse();
        result.DataResult.Should().Be(mappedDataResult);
        result.LegalResult.Should().Be(mappedLegalResult);
        result.RequiresHumanApproval.Should().BeTrue();
        result.ToolPlan.ExecutedCalls.Should().HaveCount(2);
        result.ToolPlan.ExecutedCalls
            .Should()
            .OnlyContain(call => call.Status == ToolExecutionStatus.Executed);
        publisher.PublishedEvents.Should().Contain(x =>
            x.Type == "tool_call_executed" &&
            x.Agent == "DataAgent" &&
            x.Message == "Llamada a herramienta aprobada 'data.analyze_transactions' ejecutada usando Fake Controlled Tool Executor."
        );
        publisher.PublishedEvents.Should().Contain(x =>
            x.Type == "tool_call_executed" &&
            x.Agent == "LegalAgent" &&
            x.Message == "Llamada a herramienta aprobada 'legal.search_cnv_regulation' ejecutada usando Fake Controlled Tool Executor."
        );
    }

    [Fact]
    public async Task RunAsync_Should_skip_approved_calls_and_never_call_executor_in_shadow_mode()
    {
        var dataAgent = new FakeDataAgent(CreateDataResult(hasAnomaly: true));
        var legalAgent = new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false));
        var executor = new FakeControlledToolExecutor();
        var publisher = new FakeActivityEventPublisher();
        var proposedPlan = new ToolPlan(
            [
                CreateProposedToolCall("data.analyze_transactions"),
                CreateProposedToolCall(
                    "legal.search_cnv_regulation",
                    new Dictionary<string, string>
                    {
                        ["query"] = "agentes"
                    }
                ),
                CreateProposedToolCall("workflow.complete"),
                CreateProposedToolCall("unknown.tool")
            ]
        );

        var plannerAgent = new PlannerAgent(
            dataAgent,
            legalAgent,
            publisher,
            new FakePlannerReasoningService(),
            new FakeToolPlanProposalService(proposedPlan),
            new ToolPlanNormalizer(),
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            executor,
            new FakeToolExecutionResultMapper(),
            new ToolCallingOptions
            {
                Enabled = true,
                ExecutionMode = ToolCallingExecutionMode.Shadow
            }
        );

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None
        );

        dataAgent.WasCalled.Should().BeTrue();
        legalAgent.WasCalled.Should().BeTrue();
        executor.WasCalled.Should().BeFalse();
        result.RequiresHumanApproval.Should().BeTrue();
        result.ToolPlan.ApprovedCalls.Select(call => call.ToolName)
            .Should()
            .Equal("data.analyze_transactions", "legal.search_cnv_regulation");
        result.ToolPlan.RejectedCalls.Select(call => call.ToolName)
            .Should()
            .Equal("workflow.complete", "unknown.tool");
        result.ToolPlan.ExecutedCalls
            .Should()
            .OnlyContain(call => call.Status == ToolExecutionStatus.SkippedAlreadySatisfied);
        publisher.PublishedEvents.Select(evt => evt.Type).Should().Contain(
            [
                "tool_plan_proposed",
                "tool_plan_validated",
                "tool_call_rejected",
                "tool_call_skipped"
            ]
        );
        publisher.PublishedEvents.Should().NotContain(evt => evt.Type == "tool_call_executed");
    }

    [Fact]
    public async Task RunAsync_Should_execute_only_approved_calls_in_plan_driven_mode()
    {
        var dataAgent = new FakeDataAgent(CreateDataResult(hasAnomaly: false));
        var legalAgent = new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false));
        var executor = new FakeControlledToolExecutor();
        var publisher = new FakeActivityEventPublisher();
        var mappedDataResult = CreateDataResult(hasAnomaly: true);
        var mappedLegalResult = CreateLegalResult(hasComplianceRisk: true);
        var proposedPlan = new ToolPlan(
            [
                CreateProposedToolCall("data.analyze_transactions"),
                CreateProposedToolCall(
                    "legal.search_cnv_regulation",
                    new Dictionary<string, string>
                    {
                        ["query"] = "agentes"
                    }
                ),
                CreateProposedToolCall("workflow.complete"),
                CreateProposedToolCall("money.transfer")
            ]
        );

        var plannerAgent = new PlannerAgent(
            dataAgent,
            legalAgent,
            publisher,
            new FakePlannerReasoningService(),
            new FakeToolPlanProposalService(proposedPlan),
            new ToolPlanNormalizer(),
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            executor,
            new FakeToolExecutionResultMapper(mappedDataResult, mappedLegalResult),
            CreatePlanDrivenOptions()
        );

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None
        );

        executor.WasCalled.Should().BeTrue();
        executor.ReceivedCalls.Select(call => call.ToolName)
            .Should()
            .Equal("data.analyze_transactions", "legal.search_cnv_regulation");
        dataAgent.WasCalled.Should().BeFalse();
        legalAgent.WasCalled.Should().BeFalse();
        result.ToolPlan.RejectedCalls.Select(call => call.ToolName)
            .Should()
            .Equal("workflow.complete", "money.transfer");
        result.ToolPlan.ExecutedCalls
            .Should()
            .OnlyContain(call => call.Status == ToolExecutionStatus.Executed);
        result.RequiresHumanApproval.Should().BeTrue();
        publisher.PublishedEvents.Select(evt => evt.Type).Should().Contain(
            [
                "tool_plan_proposed",
                "tool_plan_validated",
                "tool_call_rejected",
                "tool_call_executed"
            ]
        );
        publisher.PublishedEvents.Should().NotContain(evt => evt.Type == "tool_call_skipped");
    }

    [Fact]
    public async Task RunAsync_Should_map_plan_driven_tool_outputs_with_real_mapper()
    {
        var mappedDataResult = CreateDataResult(hasAnomaly: true);
        var mappedLegalOutput = CreateCitedLegalSearchResponse();
        var dataAgent = new FakeDataAgent(CreateDataResult(hasAnomaly: false));
        var legalAgent = new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false));
        var executor = new MappingControlledToolExecutor(mappedDataResult, mappedLegalOutput);

        var plannerAgent = new PlannerAgent(
            dataAgent,
            legalAgent,
            new FakeActivityEventPublisher(),
            new FakePlannerReasoningService(),
            new FakeToolPlanProposalService(CreateExecutableToolPlan()),
            new ToolPlanNormalizer(),
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            executor,
            new ToolExecutionResultMapper(),
            CreatePlanDrivenOptions()
        );

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None
        );

        executor.WasCalled.Should().BeTrue();
        dataAgent.WasCalled.Should().BeFalse();
        legalAgent.WasCalled.Should().BeFalse();
        result.DataResult.Should().BeEquivalentTo(mappedDataResult);
        result.LegalResult.HasComplianceRisk.Should().BeTrue();
        result.LegalResult.Engine.Should().Be("Semantic Kernel + MCP CNV Regulation Server");
        result.LegalResult.Evidence.Should().ContainSingle();
        result.ToolPlan.ExecutedCalls
            .Should()
            .OnlyContain(call => call.Status == ToolExecutionStatus.Executed);
        result.RequiresHumanApproval.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_Should_fallback_to_deterministic_agents_when_plan_driven_mapping_fails()
    {
        var dataAgent = new FakeDataAgent(CreateDataResult(hasAnomaly: false));
        var legalAgent = new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: true));
        var executor = new FakeControlledToolExecutor();
        var publisher = new FakeActivityEventPublisher();

        var plannerAgent = new PlannerAgent(
            dataAgent,
            legalAgent,
            publisher,
            new FakePlannerReasoningService(),
            new FakeToolPlanProposalService(CreateExecutableToolPlan()),
            new ToolPlanNormalizer(),
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            executor,
            new FakeToolExecutionResultMapper(dataResult: null, legalResult: null),
            CreatePlanDrivenOptions()
        );

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None
        );

        executor.WasCalled.Should().BeTrue();
        dataAgent.WasCalled.Should().BeTrue();
        legalAgent.WasCalled.Should().BeTrue();
        result.RequiresHumanApproval.Should().BeTrue();
        result.LegalResult.HasComplianceRisk.Should().BeTrue();
        publisher.PublishedEvents.Should().Contain(x =>
            x.Type == "tool_execution_fallback_used" &&
            x.Message == "La ejecución basada en plan falló; se utilizó la ruta determinista del agente."
        );
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
        var resolvedArguments = arguments;

        if (resolvedArguments is null && string.Equals(
                toolName,
                PlannerToolCatalog.AnalyzeTransactionsName,
                StringComparison.OrdinalIgnoreCase))
        {
            resolvedArguments = new Dictionary<string, string>
            {
                ["sessionId"] = Guid.Empty.ToString(),
                ["reportName"] = "planner-test-report",
                ["totalAmount"] = "125000",
                ["transactionCount"] = "42",
                ["submittedAt"] = DateTimeOffset.UnixEpoch.ToString("O")
            };
        }
        else if (resolvedArguments is null && string.Equals(
                     toolName,
                     PlannerToolCatalog.SearchCnvRegulationName,
                     StringComparison.OrdinalIgnoreCase))
        {
            resolvedArguments = new Dictionary<string, string>
            {
                ["query"] = "agentes"
            };
        }

        return new ProposedToolCall(
            ToolName: toolName,
            Arguments: resolvedArguments ?? new Dictionary<string, string>
            {
                ["sessionId"] = Guid.NewGuid().ToString()
            },
            Reason: reason
        );
    }

    private static ToolPlan CreateExecutableToolPlan()
    {
        return new ToolPlan(
            [
                CreateProposedToolCall(
                    "data.analyze_transactions",
                    new Dictionary<string, string>
                    {
                        ["sessionId"] = Guid.Empty.ToString(),
                        ["reportName"] = "test-report",
                        ["totalAmount"] = "125000",
                        ["transactionCount"] = "42",
                        ["submittedAt"] = "2026-05-06T14:00:00Z"
                    }
                ),
                CreateProposedToolCall(
                    "legal.search_cnv_regulation",
                    new Dictionary<string, string>
                    {
                        ["query"] = "agentes",
                        ["area"] = "Agentes",
                        ["limit"] = "5",
                        ["requiresReview"] = "true"
                    }
                )
            ]
        );
    }

    private static CnvRegulationSearchResponse CreateCitedLegalSearchResponse()
    {
        return new CnvRegulationSearchResponse(
            Query: "agentes",
            Results:
            [
                new CnvRegulationSearchResult(
                    DocumentId: "cnv-plan-driven-result",
                    ChunkId: "chunk-1",
                    Title: "Plan-driven CNV cited result",
                    Chapter: null,
                    Section: "Agentes",
                    Article: "Articulo 1",
                    Source: "CNV test fixture",
                    Url: "https://example.test/cnv",
                    Snippet: "Cited plan-driven evidence.",
                    Score: 0.9,
                    Citations:
                    [
                        new CnvRegulationCitation(
                            Source: "CNV test fixture",
                            DocumentType: "test_fixture",
                            ResolutionNumber: "PD",
                            Title: "Plan-driven CNV cited result",
                            Chapter: null,
                            Section: "Agentes",
                            Article: "Articulo 1",
                            PublicationDate: null,
                            Url: "https://example.test/cnv",
                            QuotedText: "Cited plan-driven evidence."
                        )
                    ]
                )
            ],
            Warnings: []
        );
    }

    private static ToolCallingOptions CreatePlanDrivenOptions()
    {
        return new ToolCallingOptions
        {
            Enabled = true,
            ExecutionMode = ToolCallingExecutionMode.PlanDriven
        };
    }

    private sealed class FakeDataAgent : IDataAgent
    {
        private readonly DataAgentResult _result;

        public bool WasCalled { get; private set; }
        public FinancialReportContext? ReceivedReport { get; private set; }

        public FakeDataAgent(DataAgentResult result)
        {
            _result = result;
        }

        public Task<DataAgentResult> AnalyzeAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            ReceivedReport = report;

            return Task.FromResult(_result);
        }
    }

    private sealed class FakeLegalAgent : ILegalAgent
    {
        private readonly LegalAgentResult _result;

        public bool WasCalled { get; private set; }
        public FinancialReportContext? ReceivedReport { get; private set; }

        public FakeLegalAgent(LegalAgentResult result)
        {
            _result = result;
        }

        public Task<LegalAgentResult> ReviewAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            ReceivedReport = report;

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
        public ToolPlanProposalInput? Input { get; private set; }

        public FakeToolPlanProposalService(
            ToolPlan? result = null)
        {
            _result = result ?? new ToolPlan([]);
        }

        public Task<ToolPlan> ProposeAsync(
            ToolPlanProposalInput input,
            CancellationToken cancellationToken)
        {
            Input = input;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeControlledToolExecutor : IControlledToolExecutor
    {
        public bool WasCalled { get; private set; }

        public IReadOnlyList<ApprovedToolCall> ReceivedCalls { get; private set; } = [];

        public Task<IReadOnlyList<ToolExecutionResult>> ExecuteAsync(
            IReadOnlyList<ApprovedToolCall> calls,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            ReceivedCalls = calls;

            return Task.FromResult<IReadOnlyList<ToolExecutionResult>>(
                calls.Select(call => new ToolExecutionResult(
                    ToolName: call.ToolName,
                    Status: ToolExecutionStatus.Executed,
                    Succeeded: true,
                    Summary: $"Executed {call.ToolName}.",
                    Engine: "Fake Controlled Tool Executor",
                    OutputJson: "{}",
                    Error: null
                )).ToList()
            );
        }
    }

    private sealed class FakeToolExecutionResultMapper : IToolExecutionResultMapper
    {
        private readonly DataAgentResult? _dataResult;
        private readonly LegalAgentResult? _legalResult;

        public FakeToolExecutionResultMapper(
            DataAgentResult? dataResult = null,
            LegalAgentResult? legalResult = null)
        {
            _dataResult = dataResult;
            _legalResult = legalResult;
        }

        public DataAgentResult? TryMapDataResult(
            IReadOnlyList<ToolExecutionResult> executedCalls)
        {
            return _dataResult;
        }

        public LegalAgentResult? TryMapLegalResult(
            IReadOnlyList<ToolExecutionResult> executedCalls)
        {
            return _legalResult;
        }
    }

    private sealed class MappingControlledToolExecutor : IControlledToolExecutor
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly DataAgentResult _dataResult;
        private readonly CnvRegulationSearchResponse _legalOutput;

        public bool WasCalled { get; private set; }

        public MappingControlledToolExecutor(
            DataAgentResult dataResult,
            CnvRegulationSearchResponse legalOutput)
        {
            _dataResult = dataResult;
            _legalOutput = legalOutput;
        }

        public Task<IReadOnlyList<ToolExecutionResult>> ExecuteAsync(
            IReadOnlyList<ApprovedToolCall> calls,
            CancellationToken cancellationToken)
        {
            WasCalled = true;

            return Task.FromResult<IReadOnlyList<ToolExecutionResult>>(
                calls.Select(CreateResult).ToList()
            );
        }

        private ToolExecutionResult CreateResult(
            ApprovedToolCall call)
        {
            object? output = call.ToolName switch
            {
                "data.analyze_transactions" => _dataResult,
                "legal.search_cnv_regulation" => _legalOutput,
                _ => null
            };

            if (output is null)
            {
                return new ToolExecutionResult(
                    ToolName: call.ToolName,
                    Status: ToolExecutionStatus.Failed,
                    Succeeded: false,
                    Summary: "Tool is not executable.",
                    Engine: "Mapping Controlled Tool Executor",
                    OutputJson: "{}",
                    Error: "Tool is not executable."
                );
            }

            return new ToolExecutionResult(
                ToolName: call.ToolName,
                Status: ToolExecutionStatus.Executed,
                Succeeded: true,
                Summary: $"Executed {call.ToolName}.",
                Engine: "Mapping Controlled Tool Executor",
                OutputJson: JsonSerializer.Serialize(output, JsonOptions),
                Error: null
            );
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

    private static FinancialAnalysisContext CreateFinancialAnalysisContext()
    {
        return new FinancialAnalysisContext(
            Engine: "Test financial engine",
            DocumentId: "financial-doc",
            Company: "Financial Co",
            Ratios: [],
            Comparisons: [],
            RiskSignals: [],
            RiskEvidence: [],
            Warnings: [],
            Limitations: [],
            MetricsInputSource: FinancialMetricsInputSources.SessionContext);
    }
}
