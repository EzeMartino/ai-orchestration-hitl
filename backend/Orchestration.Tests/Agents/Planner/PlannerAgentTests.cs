using System.Text.Json;
using FluentAssertions;
using Orchestration.Application.Activity;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Legal.Cnv;
using Orchestration.Application.Agents.Planner;
using Orchestration.Application.Agents.Planner.Reasoning;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.Agents.Planner.ToolCalling.Mapping;
using Orchestration.Application.Agents.Shared;
using Orchestration.Domain.AnalysisSessions;
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
    public async Task RunAsync_Should_require_approval_when_data_requires_review_without_anomaly()
    {
        var plannerAgent = new PlannerAgent(
            new FakeDataAgent(CreateDataResult(hasAnomaly: false) with
            {
                RequiresHumanReview = true
            }),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false)),
            new FakeActivityEventPublisher(),
            new FakePlannerReasoningService(),
            new FakeToolPlanProposalService(),
            new ToolPlanNormalizer(),
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            new FakeControlledToolExecutor(),
            new FakeToolExecutionResultMapper(),
            new ToolCallingOptions());

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None);

        result.DataResult.HasAnomaly.Should().BeFalse();
        result.DataResult.RequiresHumanReview.Should().BeTrue();
        result.LegalResult.HasComplianceRisk.Should().BeFalse();
        result.RequiresHumanApproval.Should().BeTrue();
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
                    new Dictionary<string, string>(),
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
    public async Task RunAsync_PlanDriven_Should_stage_data_before_legal_while_preserving_plan_order()
    {
        var dataAgent = new FakeDataAgent(CreateDataResult(hasAnomaly: false));
        var legalAgent = new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false));
        var trace = new List<string>();
        var executor = new FakeControlledToolExecutor(trace: trace);
        var publisher = new FakeActivityEventPublisher();
        var reasoning = new FakePlannerReasoningService(
            onCall: _ => trace.Add("reasoning_started"));
        var mappedDataResult = CreateDataResult(hasAnomaly: true);
        var mappedLegalResult = CreateLegalResult(hasComplianceRisk: false);
        var proposedPlan = new ToolPlan(
            [
                CreateProposedToolCall(PlannerToolCatalog.SearchCnvRegulationName),
                CreateProposedToolCall(PlannerToolCatalog.AnalyzeTransactionsName)
            ],
            ProposalSource: ToolPlanProposalSource.DeterministicFallback,
            ProposalFallbackReason: ToolPlanProposalFallbackReason.LlmResponseInvalid);

        var plannerAgent = new PlannerAgent(
            dataAgent,
            legalAgent,
            publisher,
            reasoning,
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
        executor.Invocations.Select(invocation => invocation.Calls.Single().ToolName)
            .Should().Equal(
                PlannerToolCatalog.AnalyzeTransactionsName,
                PlannerToolCatalog.SearchCnvRegulationName);
        executor.Invocations.Should().OnlyContain(invocation =>
            invocation.Calls.Count == 1);
        dataAgent.WasCalled.Should().BeFalse();
        legalAgent.WasCalled.Should().BeFalse();
        result.DataResult.Should().Be(mappedDataResult);
        result.LegalResult.Should().Be(mappedLegalResult);
        result.RequiresHumanApproval.Should().BeTrue();
        result.ToolPlan.ExecutedCalls.Should().HaveCount(2);
        result.ToolPlan.ProposedCalls.Select(call => call.ToolName).Should().Equal(
            PlannerToolCatalog.SearchCnvRegulationName,
            PlannerToolCatalog.AnalyzeTransactionsName);
        result.ToolPlan.ApprovedCalls.Select(call => call.ToolName).Should().Equal(
            PlannerToolCatalog.SearchCnvRegulationName,
            PlannerToolCatalog.AnalyzeTransactionsName);
        result.ToolPlan.ExecutedCalls.Select(call => call.ToolName).Should().Equal(
            PlannerToolCatalog.AnalyzeTransactionsName,
            PlannerToolCatalog.SearchCnvRegulationName);
        result.ToolPlan.ProposalSource.Should().Be(
            ToolPlanProposalSource.DeterministicFallback);
        result.ToolPlan.ProposalFallbackReason.Should().Be(
            ToolPlanProposalFallbackReason.LlmResponseInvalid);
        trace.Should().Equal(
            "data_started",
            "data_completed",
            "legal_started",
            "legal_completed",
            "reasoning_started");
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

        var events = publisher.PublishedEvents.ToList();
        var proposedEvent = events.FindIndex(evt => evt.Type == "tool_plan_proposed");
        var validatedEvent = events.FindIndex(evt => evt.Type == "tool_plan_validated");
        var dataEvent = events.FindIndex(evt =>
            evt.Type == "tool_call_executed" && evt.Agent == "DataAgent");
        var legalEvent = events.FindIndex(evt =>
            evt.Type == "tool_call_executed" && evt.Agent == "LegalAgent");
        var reasoningEvent = events.FindIndex(evt =>
            evt.Type == "planner_reasoning_completed");
        proposedEvent.Should().BeLessThan(validatedEvent);
        validatedEvent.Should().BeLessThan(dataEvent);
        dataEvent.Should().BeLessThan(legalEvent);
        legalEvent.Should().BeLessThan(reasoningEvent);
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
                    new Dictionary<string, string>()
                ),
                CreateProposedToolCall("workflow.complete"),
                CreateProposedToolCall("unknown.tool")
            ],
            ProposalSource: ToolPlanProposalSource.DeterministicFallback,
            ProposalFallbackReason: ToolPlanProposalFallbackReason.LlmResponseInvalid
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
        result.ToolPlan.ProposalSource.Should().Be(ToolPlanProposalSource.DeterministicFallback);
        result.ToolPlan.ProposalFallbackReason.Should().Be(ToolPlanProposalFallbackReason.LlmResponseInvalid);
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
                    new Dictionary<string, string>()
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
        executor.Invocations.SelectMany(invocation => invocation.Calls)
            .Select(call => call.ToolName)
            .Should()
            .Equal("data.analyze_transactions", "legal.search_cnv_regulation");
        executor.Invocations.Should().HaveCount(2);
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
        var mappedLegalOutput = CreateLegalResult(hasComplianceRisk: true);
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
        result.LegalResult.Engine.Should().Be(mappedLegalOutput.Engine);
        result.LegalResult.Evidence.Should().ContainSingle();
        result.ToolPlan.ExecutedCalls
            .Should()
            .OnlyContain(call => call.Status == ToolExecutionStatus.Executed);
        result.RequiresHumanApproval.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_PlanDriven_Should_use_safe_data_without_retry_when_data_execution_fails()
    {
        var dataAgent = new FakeDataAgent(CreateDataResult(hasAnomaly: false));
        var legalAgent = new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false));
        var mappedLegalResult = CreateLegalResult(hasComplianceRisk: false);
        var executor = new FakeControlledToolExecutor(
            handler: (calls, _, _) => Task.FromResult<IReadOnlyList<ToolExecutionResult>>(
                calls.Select(call => CreateToolExecutionResult(
                    call,
                    succeeded: call.ToolName != PlannerToolCatalog.AnalyzeTransactionsName))
                .ToArray()));
        var publisher = new FakeActivityEventPublisher();
        var reasoning = new FakePlannerReasoningService();

        var plannerAgent = new PlannerAgent(
            dataAgent,
            legalAgent,
            publisher,
            reasoning,
            new FakeToolPlanProposalService(CreateExecutableToolPlan()),
            new ToolPlanNormalizer(),
            new ToolPlanValidator(),
            new ToolExecutionPolicy(),
            executor,
            new FakeToolExecutionResultMapper(
                dataResult: null,
                legalResult: mappedLegalResult),
            CreatePlanDrivenOptions()
        );

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None
        );

        executor.WasCalled.Should().BeTrue();
        executor.Invocations.Should().HaveCount(2);
        executor.Invocations.Count(invocation =>
            invocation.Calls.Single().ToolName ==
                PlannerToolCatalog.AnalyzeTransactionsName).Should().Be(1);
        dataAgent.WasCalled.Should().BeFalse();
        legalAgent.WasCalled.Should().BeFalse();
        result.RequiresHumanApproval.Should().BeTrue();
        result.DataResult.Severity.Should().Be("Unknown");
        result.DataResult.RequiresHumanReview.Should().BeTrue();
        result.LegalResult.Should().Be(mappedLegalResult);
        reasoning.Input!.DataSummary.Should().Be(result.DataResult.Summary);
        reasoning.Input.LegalSummary.Should().Be(mappedLegalResult.Summary);
        var legalContext = executor.Invocations[1].RuntimeContext;
        legalContext.Should().NotBeNull();
        legalContext!.DataEvidence.DataToolStatus.Should().Be(
            LegalDataToolStatuses.Failed);
        legalContext.DataEvidence.FallbackReason.Should().Be(
            LegalCnvFallbackReasons.DataToolFailed);
        publisher.PublishedEvents.Should().NotContain(x =>
            x.Type == "tool_execution_fallback_used");
    }

    [Fact]
    public async Task RunAsync_PlanDriven_Should_use_safe_data_when_data_mapper_throws()
    {
        var mappedLegal = CreateLegalResult(hasComplianceRisk: false);
        var executor = new FakeControlledToolExecutor(
            handler: (calls, _, _) => Task.FromResult<IReadOnlyList<ToolExecutionResult>>(
                calls.Select(call => CreateToolExecutionResult(
                    call,
                    outputJson: call.ToolName == PlannerToolCatalog.AnalyzeTransactionsName
                        ? null!
                        : "{}"))
                    .ToArray()));
        var mapper = new FakeToolExecutionResultMapper(
            legalResult: mappedLegal,
            dataException: new JsonException("Malformed Data output."));
        var reasoning = new FakePlannerReasoningService();
        var plannerAgent = CreatePlanDrivenPlanner(
            CreateExecutableToolPlan(),
            new FakeDataAgent(CreateDataResult(hasAnomaly: true)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: true)),
            executor,
            mapper,
            reasoning: reasoning);

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None);

        executor.Invocations.Should().HaveCount(2);
        result.DataResult.Severity.Should().Be("Unknown");
        result.DataResult.RequiresHumanReview.Should().BeTrue();
        result.LegalResult.Should().BeSameAs(mappedLegal);
        result.ToolPlan.ExecutedCalls[0].Status.Should().Be(ToolExecutionStatus.Executed);
        result.ToolPlan.ExecutedCalls[0].OutputJson.Should().BeNull();
        reasoning.Input!.DataSummary.Should().Be(result.DataResult.Summary);
    }

    [Fact]
    public async Task RunAsync_PlanDriven_Should_use_safe_legal_when_legal_mapper_throws()
    {
        const string malformedLegalOutput = "{not-json";
        var mappedData = CreateDataResult(hasAnomaly: false);
        var executor = new FakeControlledToolExecutor(
            handler: (calls, _, _) => Task.FromResult<IReadOnlyList<ToolExecutionResult>>(
                calls.Select(call => CreateToolExecutionResult(
                    call,
                    outputJson: call.ToolName == PlannerToolCatalog.SearchCnvRegulationName
                        ? malformedLegalOutput
                        : "{}"))
                    .ToArray()));
        var mapper = new FakeToolExecutionResultMapper(
            dataResult: mappedData,
            legalException: new JsonException("Malformed Legal output."));
        var reasoning = new FakePlannerReasoningService();
        var plannerAgent = CreatePlanDrivenPlanner(
            CreateExecutableToolPlan(),
            new FakeDataAgent(CreateDataResult(hasAnomaly: true)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: true)),
            executor,
            mapper,
            reasoning: reasoning);

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None);

        executor.Invocations.Should().HaveCount(2);
        result.DataResult.Should().BeSameAs(mappedData);
        result.LegalResult.RiskLevel.Should().Be("Unknown");
        result.LegalResult.RequiresHumanReview.Should().BeTrue();
        result.ToolPlan.ExecutedCalls[1].Status.Should().Be(ToolExecutionStatus.Executed);
        result.ToolPlan.ExecutedCalls[1].OutputJson.Should().Be(malformedLegalOutput);
        reasoning.Input!.LegalSummary.Should().Be(result.LegalResult.Summary);
    }

    [Fact]
    public async Task RunAsync_PlanDriven_Should_propagate_mapper_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var expectedException = new OperationCanceledException(cancellation.Token);
        var executor = new FakeControlledToolExecutor();
        var mapper = new FakeToolExecutionResultMapper(
            dataException: expectedException);
        var plannerAgent = CreatePlanDrivenPlanner(
            CreateExecutableToolPlan(),
            new FakeDataAgent(CreateDataResult(hasAnomaly: true)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: true)),
            executor,
            mapper);

        Func<Task> act = () => plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            cancellation.Token);

        var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.Should().BeSameAs(expectedException);
        executor.Invocations.Should().ContainSingle(invocation =>
            invocation.Calls.Single().ToolName ==
                PlannerToolCatalog.AnalyzeTransactionsName);
    }

    [Fact]
    public async Task RunAsync_PlanDriven_Should_pass_mapped_data_context_to_legal_and_reasoning()
    {
        var report = TestFinancialReport.CreateContext();
        var mappedData = CreateDataResult(hasAnomaly: false) with
        {
            Summary = "Mapped data final.",
            FinancialAnalysis = CreateFinancialAnalysisContext()
        };
        var mappedLegal = CreateLegalResult(hasComplianceRisk: false) with
        {
            Summary = "Mapped legal final."
        };
        var executor = new FakeControlledToolExecutor();
        var mapper = new FakeToolExecutionResultMapper(mappedData, mappedLegal);
        var reasoning = new FakePlannerReasoningService();
        var dataAgent = new FakeDataAgent(CreateDataResult(hasAnomaly: true));
        var legalAgent = new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: true));
        var plannerAgent = CreatePlanDrivenPlanner(
            CreateExecutableToolPlan(),
            dataAgent,
            legalAgent,
            executor,
            mapper,
            reasoning: reasoning);

        var result = await plannerAgent.RunAsync(report, CancellationToken.None);

        executor.Invocations.Should().HaveCount(2);
        executor.Invocations[0].RuntimeContext.Should().BeNull();
        var legalContext = executor.Invocations[1].RuntimeContext;
        legalContext.Should().NotBeNull();
        legalContext!.Report.Should().Be(report);
        legalContext.DataResult.Should().BeSameAs(mappedData);
        legalContext.DataEvidence.DataToolStatus.Should().Be(
            LegalDataToolStatuses.Executed);
        mapper.DataAudits.Should().ContainSingle()
            .Which.Select(call => call.ToolName).Should().Equal(
                PlannerToolCatalog.AnalyzeTransactionsName);
        mapper.LegalAudits.Should().ContainSingle()
            .Which.Select(call => call.ToolName).Should().Equal(
                PlannerToolCatalog.SearchCnvRegulationName);
        reasoning.Input!.DataSummary.Should().Be("Mapped data final.");
        reasoning.Input.LegalSummary.Should().Be("Mapped legal final.");
        result.DataResult.Should().BeSameAs(mappedData);
        result.LegalResult.Should().BeSameAs(mappedLegal);
        dataAgent.WasCalled.Should().BeFalse();
        legalAgent.WasCalled.Should().BeFalse();
    }

    [Theory]
    [InlineData(DataStagePlanCase.Missing)]
    [InlineData(DataStagePlanCase.Rejected)]
    [InlineData(DataStagePlanCase.PolicyDenied)]
    public async Task RunAsync_PlanDriven_Should_run_legal_once_when_data_stage_is_unavailable(
        DataStagePlanCase planCase)
    {
        var plan = planCase switch
        {
            DataStagePlanCase.Missing => new ToolPlan(
                [CreateProposedToolCall(PlannerToolCatalog.SearchCnvRegulationName)]),
            DataStagePlanCase.Rejected => new ToolPlan(
                [
                    CreateProposedToolCall(
                        PlannerToolCatalog.AnalyzeTransactionsName,
                        new Dictionary<string, string>()),
                    CreateProposedToolCall(PlannerToolCatalog.SearchCnvRegulationName)
                ]),
            _ => CreateExecutableToolPlan()
        };
        IToolExecutionPolicy policy = planCase == DataStagePlanCase.PolicyDenied
            ? new DenyingToolExecutionPolicy(
                PlannerToolCatalog.AnalyzeTransactionsName)
            : new ToolExecutionPolicy();
        var executor = new FakeControlledToolExecutor();
        var mappedLegal = CreateLegalResult(hasComplianceRisk: false);
        var plannerAgent = CreatePlanDrivenPlanner(
            plan,
            new FakeDataAgent(CreateDataResult(hasAnomaly: true)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: true)),
            executor,
            new FakeToolExecutionResultMapper(null, mappedLegal),
            policy: policy);

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None);

        executor.Invocations.Should().ContainSingle();
        executor.Invocations[0].Calls.Should().ContainSingle()
            .Which.ToolName.Should().Be(PlannerToolCatalog.SearchCnvRegulationName);
        var context = executor.Invocations[0].RuntimeContext;
        context.Should().NotBeNull();
        var expectedStatus = planCase == DataStagePlanCase.PolicyDenied
            ? LegalDataToolStatuses.Failed
            : LegalDataToolStatuses.Absent;
        var expectedReason = planCase == DataStagePlanCase.PolicyDenied
            ? LegalCnvFallbackReasons.DataToolFailed
            : LegalCnvFallbackReasons.DataStageAbsent;
        context!.DataEvidence.DataToolStatus.Should().Be(expectedStatus);
        context.DataEvidence.FallbackReason.Should().Be(expectedReason);
        result.DataResult.Severity.Should().Be("Unknown");
        result.DataResult.RequiresHumanReview.Should().BeTrue();
        result.LegalResult.Should().BeSameAs(mappedLegal);
    }

    [Theory]
    [InlineData(LegalStageOutcome.Missing)]
    [InlineData(LegalStageOutcome.Rejected)]
    [InlineData(LegalStageOutcome.PolicyDenied)]
    [InlineData(LegalStageOutcome.Failed)]
    [InlineData(LegalStageOutcome.Malformed)]
    public async Task RunAsync_PlanDriven_Should_use_safe_legal_without_deterministic_retry(
        LegalStageOutcome outcome)
    {
        var plan = outcome switch
        {
            LegalStageOutcome.Missing => new ToolPlan(
                [CreateProposedToolCall(PlannerToolCatalog.AnalyzeTransactionsName)]),
            LegalStageOutcome.Rejected => new ToolPlan(
                [
                    CreateProposedToolCall(PlannerToolCatalog.AnalyzeTransactionsName),
                    CreateProposedToolCall(
                        PlannerToolCatalog.SearchCnvRegulationName,
                        new Dictionary<string, string> { ["query"] = "hostile" })
                ]),
            _ => CreateExecutableToolPlan()
        };
        IToolExecutionPolicy policy = outcome == LegalStageOutcome.PolicyDenied
            ? new DenyingToolExecutionPolicy(
                PlannerToolCatalog.SearchCnvRegulationName)
            : new ToolExecutionPolicy();
        var executor = new FakeControlledToolExecutor(
            handler: (calls, _, _) => Task.FromResult<IReadOnlyList<ToolExecutionResult>>(
                calls.Select(call => CreateToolExecutionResult(
                    call,
                    succeeded: outcome != LegalStageOutcome.Failed ||
                        call.ToolName != PlannerToolCatalog.SearchCnvRegulationName))
                .ToArray()));
        var dataAgent = new FakeDataAgent(CreateDataResult(hasAnomaly: true));
        var legalAgent = new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: true));
        var reasoning = new FakePlannerReasoningService();
        var plannerAgent = CreatePlanDrivenPlanner(
            plan,
            dataAgent,
            legalAgent,
            executor,
            new FakeToolExecutionResultMapper(
                CreateDataResult(hasAnomaly: false),
                legalResult: null),
            reasoning: reasoning,
            policy: policy);

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None);

        var expectedExecutions = outcome is LegalStageOutcome.Failed or
            LegalStageOutcome.Malformed
            ? 2
            : 1;
        executor.Invocations.Should().HaveCount(expectedExecutions);
        executor.Invocations.Count(invocation =>
            invocation.Calls.Single().ToolName ==
                PlannerToolCatalog.SearchCnvRegulationName).Should().Be(
                    expectedExecutions - 1);
        result.LegalResult.RiskLevel.Should().Be("Unknown");
        result.LegalResult.Evidence.Should().BeEmpty();
        result.LegalResult.RequiresHumanReview.Should().BeTrue();
        reasoning.Input!.LegalSummary.Should().Be(result.LegalResult.Summary);
        result.RequiresHumanApproval.Should().BeTrue();
        dataAgent.WasCalled.Should().BeFalse();
        legalAgent.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_PlanDriven_Should_classify_successful_unmappable_data_as_missing_financial_analysis()
    {
        var executor = new FakeControlledToolExecutor();
        var dataAgent = new FakeDataAgent(CreateDataResult(hasAnomaly: true));
        var legalAgent = new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: true));
        var mappedLegal = CreateLegalResult(hasComplianceRisk: false);
        var plannerAgent = CreatePlanDrivenPlanner(
            CreateExecutableToolPlan(),
            dataAgent,
            legalAgent,
            executor,
            new FakeToolExecutionResultMapper(null, mappedLegal));

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None);

        executor.Invocations.Should().HaveCount(2);
        executor.Invocations.Count(invocation =>
            invocation.Calls.Single().ToolName ==
                PlannerToolCatalog.AnalyzeTransactionsName).Should().Be(1);
        var context = executor.Invocations[1].RuntimeContext!;
        context.DataEvidence.DataToolStatus.Should().Be(
            LegalDataToolStatuses.Executed);
        context.DataEvidence.FallbackReason.Should().Be(
            LegalCnvFallbackReasons.FinancialAnalysisMissing);
        result.DataResult.Severity.Should().Be("Unknown");
        dataAgent.WasCalled.Should().BeFalse();
        legalAgent.WasCalled.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RunAsync_PlanDriven_Should_require_approval_for_stage_review_flags(
        bool dataRequiresReview,
        bool legalRequiresReview)
    {
        var mappedData = CreateDataResult(hasAnomaly: false) with
        {
            RequiresHumanReview = dataRequiresReview
        };
        var mappedLegal = CreateLegalResult(hasComplianceRisk: false) with
        {
            RequiresHumanReview = legalRequiresReview
        };
        var plannerAgent = CreatePlanDrivenPlanner(
            CreateExecutableToolPlan(),
            new FakeDataAgent(CreateDataResult(hasAnomaly: true)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: true)),
            new FakeControlledToolExecutor(),
            new FakeToolExecutionResultMapper(mappedData, mappedLegal));

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None);

        result.RequiresHumanApproval.Should().BeTrue();
    }

    [Theory]
    [InlineData(PlanDrivenCancellationPoint.DataExecutor)]
    [InlineData(PlanDrivenCancellationPoint.DataEvent)]
    [InlineData(PlanDrivenCancellationPoint.LegalExecutor)]
    [InlineData(PlanDrivenCancellationPoint.LegalEvent)]
    public async Task RunAsync_PlanDriven_Should_stop_at_cancellation_boundary(
        PlanDrivenCancellationPoint cancellationPoint)
    {
        using var cancellation = new CancellationTokenSource();
        var publisher = new CallbackActivityEventPublisher(activityEvent =>
        {
            if (cancellationPoint == PlanDrivenCancellationPoint.DataEvent &&
                activityEvent.Type == "tool_call_executed" &&
                activityEvent.Agent == "DataAgent" ||
                cancellationPoint == PlanDrivenCancellationPoint.LegalEvent &&
                activityEvent.Type == "tool_call_executed" &&
                activityEvent.Agent == "LegalAgent")
            {
                cancellation.Cancel();
            }
        });
        var executor = new FakeControlledToolExecutor(
            handler: (calls, _, _) =>
            {
                var toolName = calls.Single().ToolName;
                if (cancellationPoint == PlanDrivenCancellationPoint.DataExecutor &&
                    toolName == PlannerToolCatalog.AnalyzeTransactionsName ||
                    cancellationPoint == PlanDrivenCancellationPoint.LegalExecutor &&
                    toolName == PlannerToolCatalog.SearchCnvRegulationName)
                {
                    cancellation.Cancel();
                    return Task.FromCanceled<IReadOnlyList<ToolExecutionResult>>(
                        cancellation.Token);
                }

                return Task.FromResult<IReadOnlyList<ToolExecutionResult>>(
                    calls.Select(call => CreateToolExecutionResult(call)).ToArray());
            });
        var reasoning = new FakePlannerReasoningService();
        var plannerAgent = CreatePlanDrivenPlanner(
            CreateExecutableToolPlan(),
            new FakeDataAgent(CreateDataResult(hasAnomaly: true)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: true)),
            executor,
            new FakeToolExecutionResultMapper(
                CreateDataResult(hasAnomaly: false),
                CreateLegalResult(hasComplianceRisk: false)),
            publisher,
            reasoning);

        Func<Task> act = () => plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        reasoning.WasCalled.Should().BeFalse();
        publisher.PublishedEvents.Should().NotContain(evt =>
            evt.Type == "planner_reasoning_completed" ||
            evt.Type == "planner_reasoning_fallback_used" ||
            evt.Type == "agent_completed" && evt.Agent == "PlannerAgent");
        var expectedExecutions = cancellationPoint is
            PlanDrivenCancellationPoint.LegalExecutor or
            PlanDrivenCancellationPoint.LegalEvent
            ? 2
            : 1;
        executor.Invocations.Should().HaveCount(expectedExecutions);
        if (cancellationPoint != PlanDrivenCancellationPoint.LegalEvent)
        {
            publisher.PublishedEvents.Should().NotContain(evt =>
                evt.Type == "tool_call_executed" && evt.Agent == "LegalAgent");
        }
    }

    [Fact]
    public async Task RunAsync_PlanDriven_Should_stop_after_started_event_cancels()
    {
        using var cancellation = new CancellationTokenSource();
        var publisher = new CallbackActivityEventPublisher(activityEvent =>
        {
            if (activityEvent.Type == "agent_started" &&
                activityEvent.Agent == "PlannerAgent")
            {
                cancellation.Cancel();
            }
        });
        var proposal = new FakeToolPlanProposalService(CreateExecutableToolPlan());
        var normalizer = new TrackingToolPlanNormalizer();
        var validator = new TrackingToolPlanValidator();
        var policy = new CallbackToolExecutionPolicy(approvedCalls =>
            approvedCalls.Select(call => new ToolExecutionPolicyDecision(
                call,
                ToolExecutionStatus.Executed,
                "Approved."))
                .ToArray());
        var plannerAgent = new PlannerAgent(
            new FakeDataAgent(CreateDataResult(hasAnomaly: true)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: true)),
            publisher,
            new FakePlannerReasoningService(),
            proposal,
            normalizer,
            validator,
            policy,
            new FakeControlledToolExecutor(),
            new FakeToolExecutionResultMapper(),
            CreatePlanDrivenOptions());

        Func<Task> act = () => plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        proposal.Input.Should().BeNull();
        normalizer.WasCalled.Should().BeFalse();
        validator.WasCalled.Should().BeFalse();
        policy.WasCalled.Should().BeFalse();
        publisher.PublishedEvents.Should().ContainSingle(evt =>
            evt.Type == "agent_started" && evt.Agent == "PlannerAgent");
    }

    [Fact]
    public async Task RunAsync_PlanDriven_Should_stop_after_proposal_cancels()
    {
        using var cancellation = new CancellationTokenSource();
        var proposal = new FakeToolPlanProposalService(
            CreateExecutableToolPlan(),
            cancellationToken =>
            {
                cancellationToken.Should().Be(cancellation.Token);
                cancellation.Cancel();
            });
        var normalizer = new TrackingToolPlanNormalizer();
        var validator = new TrackingToolPlanValidator();
        var policy = new CallbackToolExecutionPolicy(approvedCalls =>
            approvedCalls.Select(call => new ToolExecutionPolicyDecision(
                call,
                ToolExecutionStatus.Executed,
                "Approved."))
                .ToArray());
        var publisher = new FakeActivityEventPublisher();
        var plannerAgent = new PlannerAgent(
            new FakeDataAgent(CreateDataResult(hasAnomaly: true)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: true)),
            publisher,
            new FakePlannerReasoningService(),
            proposal,
            normalizer,
            validator,
            policy,
            new FakeControlledToolExecutor(),
            new FakeToolExecutionResultMapper(),
            CreatePlanDrivenOptions());

        Func<Task> act = () => plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        proposal.Input.Should().NotBeNull();
        normalizer.WasCalled.Should().BeFalse();
        validator.WasCalled.Should().BeFalse();
        policy.WasCalled.Should().BeFalse();
        publisher.PublishedEvents.Should().NotContain(evt =>
            evt.Type == "tool_plan_proposed" || evt.Type == "tool_plan_validated");
    }

    [Fact]
    public async Task RunAsync_PlanDriven_Should_stop_when_reasoning_cancels_before_returning()
    {
        using var cancellation = new CancellationTokenSource();
        var publisher = new FakeActivityEventPublisher();
        var executor = new FakeControlledToolExecutor();
        var reasoning = new FakePlannerReasoningService(
            onCall: cancellationToken =>
            {
                cancellationToken.Should().Be(cancellation.Token);
                cancellation.Cancel();
            });
        var plannerAgent = CreatePlanDrivenPlanner(
            CreateExecutableToolPlan(),
            new FakeDataAgent(CreateDataResult(hasAnomaly: true)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: true)),
            executor,
            new FakeToolExecutionResultMapper(
                CreateDataResult(hasAnomaly: false),
                CreateLegalResult(hasComplianceRisk: false)),
            publisher,
            reasoning);

        Func<Task> act = () => plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        reasoning.WasCalled.Should().BeTrue();
        executor.Invocations.Select(invocation => invocation.Calls.Single().ToolName)
            .Should().Equal(
                PlannerToolCatalog.AnalyzeTransactionsName,
                PlannerToolCatalog.SearchCnvRegulationName);
        publisher.PublishedEvents.Should().NotContain(evt =>
            evt.Type == "planner_reasoning_completed" ||
            evt.Type == "planner_reasoning_fallback_used" ||
            evt.Type == "planner_completed" ||
            evt.Type == "analysis_completed" ||
            evt.Type == "agent_completed" && evt.Agent == "PlannerAgent");
    }

    [Fact]
    public void PartitionPolicyDecisions_Should_classify_every_decision_once_in_stable_order()
    {
        const string unsupportedTool = "future.unsupported";
        const string deniedUnsupportedTool = "future.denied";
        var dataCall = new ApprovedToolCall(
            PlannerToolCatalog.AnalyzeTransactionsName,
            new Dictionary<string, string>(),
            "Analyze data.");
        var unsupportedCall = new ApprovedToolCall(
            unsupportedTool,
            new Dictionary<string, string>(),
            "Unsupported future call.");
        var legalCall = new ApprovedToolCall(
            PlannerToolCatalog.SearchCnvRegulationName,
            new Dictionary<string, string>(),
            "Review legal evidence.");
        var deniedUnsupportedCall = new ApprovedToolCall(
            deniedUnsupportedTool,
            new Dictionary<string, string>(),
            "Denied future call.");
        ToolExecutionPolicyDecision[] decisions =
        [
            new(dataCall, ToolExecutionStatus.Executed, "Approved by test policy."),
            new(unsupportedCall, ToolExecutionStatus.Executed, "Approved by test policy."),
            new(legalCall, ToolExecutionStatus.Executed, "Approved by test policy."),
            new(deniedUnsupportedCall, ToolExecutionStatus.Failed, "Denied by test policy.")
        ];
        var partitions = PlannerAgent.PartitionPolicyDecisions(decisions);

        partitions.Data.Should().Equal(decisions[0]);
        partitions.Legal.Should().Equal(decisions[2]);
        partitions.Unsupported.Should().Equal(decisions[1], decisions[3]);
        partitions.Data.Concat(partitions.Legal).Concat(partitions.Unsupported)
            .Should().HaveCount(decisions.Length)
            .And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task BuildExecutionAuditAsync_Should_fail_requested_call_when_executor_returns_another_tool()
    {
        const string sensitiveOutput = "executor-secret";
        var dataCall = CreateApprovedToolCall(PlannerToolCatalog.AnalyzeTransactionsName);
        var returnedLegal = CreateToolExecutionResult(
            CreateApprovedToolCall(PlannerToolCatalog.SearchCnvRegulationName),
            outputJson: sensitiveOutput);
        var executor = new FakeControlledToolExecutor(
            handler: (_, _, _) => Task.FromResult<IReadOnlyList<ToolExecutionResult>>(
                [returnedLegal]));

        var audit = await InvokeBuildExecutionAuditAsync(
            CreatePlannerForExecutionAudit(executor),
            [new(dataCall, ToolExecutionStatus.Executed, "Approved.")]);

        audit.Should().ContainSingle();
        audit[0].ToolName.Should().Be(dataCall.ToolName);
        audit[0].Status.Should().Be(ToolExecutionStatus.Failed);
        audit[0].Succeeded.Should().BeFalse();
        audit[0].Error.Should().Be("Controlled tool executor returned unexpected results.");
        audit[0].OutputJson.Should().Be("{}");
        audit[0].OutputJson.Should().NotContain(sensitiveOutput);
    }

    [Fact]
    public async Task BuildExecutionAuditAsync_Should_reconcile_reordered_results_by_tool_identity()
    {
        var dataCall = CreateApprovedToolCall(PlannerToolCatalog.AnalyzeTransactionsName);
        var legalCall = CreateApprovedToolCall(PlannerToolCatalog.SearchCnvRegulationName);
        var dataResult = CreateToolExecutionResult(dataCall, outputJson: "data-output");
        var legalResult = CreateToolExecutionResult(legalCall, outputJson: "legal-output");
        var executor = new FakeControlledToolExecutor(
            handler: (_, _, _) => Task.FromResult<IReadOnlyList<ToolExecutionResult>>(
                [legalResult, dataResult]));

        var audit = await InvokeBuildExecutionAuditAsync(
            CreatePlannerForExecutionAudit(executor),
            [
                new(dataCall, ToolExecutionStatus.Executed, "Approved."),
                new(legalCall, ToolExecutionStatus.Executed, "Approved.")
            ]);

        audit.Should().Equal(dataResult, legalResult);
    }

    [Fact]
    public async Task BuildExecutionAuditAsync_Should_preserve_matches_and_fail_missing_results()
    {
        var dataCall = CreateApprovedToolCall(PlannerToolCatalog.AnalyzeTransactionsName);
        var legalCall = CreateApprovedToolCall(PlannerToolCatalog.SearchCnvRegulationName);
        var legalResult = CreateToolExecutionResult(legalCall, outputJson: "legal-output");
        var executor = new FakeControlledToolExecutor(
            handler: (_, _, _) => Task.FromResult<IReadOnlyList<ToolExecutionResult>>(
                [legalResult]));

        var audit = await InvokeBuildExecutionAuditAsync(
            CreatePlannerForExecutionAudit(executor),
            [
                new(dataCall, ToolExecutionStatus.Executed, "Approved."),
                new(legalCall, ToolExecutionStatus.Executed, "Approved.")
            ]);

        audit.Should().HaveCount(2);
        audit[0].ToolName.Should().Be(dataCall.ToolName);
        audit[0].Status.Should().Be(ToolExecutionStatus.Failed);
        audit[0].Error.Should().Be("Controlled tool executor returned no matching result.");
        audit[0].OutputJson.Should().Be("{}");
        audit[1].Should().BeSameAs(legalResult);
    }

    [Fact]
    public async Task BuildExecutionAuditAsync_Should_fail_batch_when_executor_returns_extra_results()
    {
        var dataCall = CreateApprovedToolCall(PlannerToolCatalog.AnalyzeTransactionsName);
        var legalCall = CreateApprovedToolCall(PlannerToolCatalog.SearchCnvRegulationName);
        var dataResult = CreateToolExecutionResult(dataCall, outputJson: "data-secret");
        var legalResult = CreateToolExecutionResult(legalCall, outputJson: "legal-secret");
        var duplicateData = CreateToolExecutionResult(dataCall, outputJson: "duplicate-secret");
        var nullNamedResult = legalResult with { ToolName = null!, OutputJson = "null-secret" };
        var executor = new FakeControlledToolExecutor(
            handler: (_, _, _) => Task.FromResult<IReadOnlyList<ToolExecutionResult>>(
                [legalResult, dataResult, duplicateData, nullNamedResult]));

        var audit = await InvokeBuildExecutionAuditAsync(
            CreatePlannerForExecutionAudit(executor),
            [
                new(dataCall, ToolExecutionStatus.Executed, "Approved."),
                new(legalCall, ToolExecutionStatus.Executed, "Approved.")
            ]);

        audit.Should().HaveCount(2);
        audit.Select(result => result.ToolName).Should().Equal(
            dataCall.ToolName,
            legalCall.ToolName);
        audit.Should().OnlyContain(result =>
            result.Status == ToolExecutionStatus.Failed &&
            !result.Succeeded &&
            result.Error == "Controlled tool executor returned unexpected results." &&
            result.OutputJson == "{}");
    }

    [Fact]
    public async Task BuildExecutionAuditAsync_Should_audit_denied_decisions_without_executor()
    {
        var executor = new FakeControlledToolExecutor();
        var call = CreateApprovedToolCall(PlannerToolCatalog.AnalyzeTransactionsName);

        var audit = await InvokeBuildExecutionAuditAsync(
            CreatePlannerForExecutionAudit(executor),
            [new(call, ToolExecutionStatus.Failed, "Denied by policy.")]);

        executor.WasCalled.Should().BeFalse();
        audit.Should().ContainSingle();
        audit[0].ToolName.Should().Be(call.ToolName);
        audit[0].Status.Should().Be(ToolExecutionStatus.Failed);
        audit[0].Error.Should().Be("Denied by policy.");
    }

    [Fact]
    public void ReconcilePolicyDecisions_Should_accept_reordered_decisions_with_same_call_references()
    {
        var dataCall = CreateApprovedToolCall(PlannerToolCatalog.AnalyzeTransactionsName);
        var legalCall = CreateApprovedToolCall(PlannerToolCatalog.SearchCnvRegulationName);
        ApprovedToolCall[] approvedCalls = [dataCall, legalCall];
        ToolExecutionPolicyDecision[] decisions =
        [
            new(legalCall, ToolExecutionStatus.Executed, "Legal approved."),
            new(dataCall, ToolExecutionStatus.Executed, "Data approved.")
        ];

        var reconciled = PlannerAgent.ReconcilePolicyDecisions(
            approvedCalls,
            decisions);

        reconciled.Should().BeSameAs(decisions);
        reconciled[0].Should().BeSameAs(decisions[0]);
        reconciled[1].Should().BeSameAs(decisions[1]);
    }

    [Fact]
    public void ReconcilePolicyDecisions_Should_reject_value_equal_cloned_calls()
    {
        var dataCall = CreateApprovedToolCall(PlannerToolCatalog.AnalyzeTransactionsName);
        var legalCall = CreateApprovedToolCall(PlannerToolCatalog.SearchCnvRegulationName);
        ApprovedToolCall[] approvedCalls = [dataCall, legalCall];
        ToolExecutionPolicyDecision[] decisions =
        [
            new(dataCall with { }, ToolExecutionStatus.Executed, "Data approved."),
            new(legalCall with { }, ToolExecutionStatus.Executed, "Legal approved.")
        ];

        var reconciled = PlannerAgent.ReconcilePolicyDecisions(
            approvedCalls,
            decisions);

        AssertPolicyReconciliationFailure(reconciled, approvedCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReconcilePolicyDecisions_Should_reject_missing_or_duplicate_decisions(
        bool duplicateFirstDecision)
    {
        var dataCall = CreateApprovedToolCall(PlannerToolCatalog.AnalyzeTransactionsName);
        var legalCall = CreateApprovedToolCall(PlannerToolCatalog.SearchCnvRegulationName);
        ApprovedToolCall[] approvedCalls = [dataCall, legalCall];
        ToolExecutionPolicyDecision[] decisions = duplicateFirstDecision
            ?
            [
                new(dataCall, ToolExecutionStatus.Executed, "Data approved once."),
                new(dataCall, ToolExecutionStatus.Executed, "Data approved twice.")
            ]
            : [new(dataCall, ToolExecutionStatus.Executed, "Data approved.")];

        var reconciled = PlannerAgent.ReconcilePolicyDecisions(
            approvedCalls,
            decisions);

        AssertPolicyReconciliationFailure(reconciled, approvedCalls);
    }

    [Fact]
    public void ReconcilePolicyDecisions_Should_count_repeated_call_references_by_occurrence()
    {
        var repeatedCall = CreateApprovedToolCall(
            PlannerToolCatalog.AnalyzeTransactionsName);
        ApprovedToolCall[] approvedCalls = [repeatedCall, repeatedCall];
        ToolExecutionPolicyDecision[] decisions =
        [
            new(repeatedCall, ToolExecutionStatus.Executed, "First occurrence."),
            new(repeatedCall, ToolExecutionStatus.Executed, "Second occurrence.")
        ];

        var reconciled = PlannerAgent.ReconcilePolicyDecisions(
            approvedCalls,
            decisions);

        reconciled.Should().BeSameAs(decisions);
        reconciled.Should().HaveCount(2);
        reconciled.Should().OnlyContain(decision =>
            ReferenceEquals(decision.Call, repeatedCall));
    }

    [Theory]
    [InlineData(ToolExecutionStatus.Executed)]
    [InlineData(ToolExecutionStatus.SkippedAlreadySatisfied)]
    [InlineData(ToolExecutionStatus.SkippedDisabled)]
    [InlineData(ToolExecutionStatus.Failed)]
    public void ReconcilePolicyDecisions_Should_preserve_allowed_statuses(
        ToolExecutionStatus status)
    {
        var call = CreateApprovedToolCall(PlannerToolCatalog.AnalyzeTransactionsName);
        ToolExecutionPolicyDecision[] decisions =
        [new(call, status, "Valid status.")];

        var reconciled = PlannerAgent.ReconcilePolicyDecisions([call], decisions);

        reconciled.Should().BeSameAs(decisions);
        reconciled[0].Status.Should().Be(status);
    }

    [Fact]
    public async Task RunAsync_PlanDriven_Should_fail_closed_when_policy_returns_unknown_status()
    {
        const string mismatchReason =
            "Tool execution policy returned decisions inconsistent with validation.";
        var executor = new FakeControlledToolExecutor();
        var publisher = new FakeActivityEventPublisher();
        var mapper = new FakeToolExecutionResultMapper();
        var policy = new CallbackToolExecutionPolicy(approvedCalls =>
        [
            new(
                approvedCalls.Single(),
                (ToolExecutionStatus)999,
                "Unrecognized status.")
        ]);
        var plannerAgent = CreatePlanDrivenPlanner(
            new ToolPlan(
                [CreateProposedToolCall(PlannerToolCatalog.AnalyzeTransactionsName)]),
            new FakeDataAgent(CreateDataResult(hasAnomaly: true)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: true)),
            executor,
            mapper,
            publisher,
            policy: policy);

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None);

        executor.WasCalled.Should().BeFalse();
        result.ToolPlan.ExecutedCalls.Should().ContainSingle();
        result.ToolPlan.ExecutedCalls[0].ToolName.Should().Be(
            PlannerToolCatalog.AnalyzeTransactionsName);
        result.ToolPlan.ExecutedCalls[0].Status.Should().Be(ToolExecutionStatus.Failed);
        result.ToolPlan.ExecutedCalls[0].Succeeded.Should().BeFalse();
        result.ToolPlan.ExecutedCalls[0].Error.Should().Be(mismatchReason);
        mapper.DataAudits.Should().ContainSingle();
        mapper.DataAudits[0].Should().OnlyContain(audit =>
            audit.Status == ToolExecutionStatus.Failed && !audit.Succeeded);
        result.DataResult.RequiresHumanReview.Should().BeTrue();
        publisher.PublishedEvents.Should().ContainSingle(evt =>
            evt.Type == "tool_call_failed" &&
            evt.Agent == "DataAgent" &&
            evt.Message.Contains(mismatchReason, StringComparison.Ordinal));
        publisher.PublishedEvents.Should().NotContain(evt =>
            evt.Type == "tool_call_executed" && evt.Agent == "DataAgent");
    }

    [Fact]
    public async Task RunAsync_PlanDriven_Should_fail_closed_when_policy_injects_supported_call()
    {
        var executor = new FakeControlledToolExecutor();
        var plan = new ToolPlan(
            [CreateProposedToolCall(PlannerToolCatalog.SearchCnvRegulationName)]);
        var policy = new CallbackToolExecutionPolicy(approvedCalls =>
        {
            var approvedLegal = approvedCalls.Single();
            var injectedData = CreateApprovedToolCall(
                PlannerToolCatalog.AnalyzeTransactionsName);
            return
            [
                new(injectedData, ToolExecutionStatus.Executed, "Injected."),
                new(approvedLegal, ToolExecutionStatus.Executed, "Approved.")
            ];
        });
        var plannerAgent = CreatePlanDrivenPlanner(
            plan,
            new FakeDataAgent(CreateDataResult(hasAnomaly: true)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: true)),
            executor,
            new FakeToolExecutionResultMapper(),
            policy: policy);

        var result = await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None);

        executor.WasCalled.Should().BeFalse();
        result.ToolPlan.ApprovedCalls.Should().ContainSingle(call =>
            call.ToolName == PlannerToolCatalog.SearchCnvRegulationName);
        result.ToolPlan.ExecutedCalls.Should().ContainSingle();
        result.ToolPlan.ExecutedCalls[0].ToolName.Should().Be(
            PlannerToolCatalog.SearchCnvRegulationName);
        result.ToolPlan.ExecutedCalls[0].Status.Should().Be(ToolExecutionStatus.Failed);
        result.ToolPlan.ExecutedCalls[0].Error.Should().Be(
            "Tool execution policy returned decisions inconsistent with validation.");
    }

    [Fact]
    public async Task RunAsync_PlanDriven_Should_throw_when_terminal_completion_publish_cancels()
    {
        using var cancellation = new CancellationTokenSource();
        var publisher = new CallbackActivityEventPublisher(activityEvent =>
        {
            if (activityEvent.Type == "agent_completed" &&
                activityEvent.Agent == "PlannerAgent")
            {
                cancellation.Cancel();
            }
        });
        var plannerAgent = CreatePlanDrivenPlanner(
            CreateExecutableToolPlan(),
            new FakeDataAgent(CreateDataResult(hasAnomaly: true)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: true)),
            new FakeControlledToolExecutor(),
            new FakeToolExecutionResultMapper(
                CreateDataResult(hasAnomaly: false),
                CreateLegalResult(hasComplianceRisk: false)),
            publisher);

        Func<Task> act = () => plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        publisher.PublishedEvents.Should().ContainSingle(evt =>
            evt.Type == "agent_completed" && evt.Agent == "PlannerAgent");
        publisher.PublishedEvents.Last().Type.Should().Be("agent_completed");
        publisher.PublishedEvents.Should().NotContain(evt =>
            evt.Type == "analysis_completed" || evt.Type == "planner_completed");
    }

    [Fact]
    public async Task RunAsync_PlanDriven_Should_publish_rejections_before_data_execution()
    {
        var trace = new List<string>();
        var publisher = new CallbackActivityEventPublisher(activityEvent =>
            trace.Add(activityEvent.Type == "tool_call_rejected"
                ? "rejected"
                : activityEvent.Type));
        var executor = new FakeControlledToolExecutor(trace: trace);
        var plan = new ToolPlan(
            [
                CreateProposedToolCall("workflow.complete"),
                CreateProposedToolCall(PlannerToolCatalog.AnalyzeTransactionsName),
                CreateProposedToolCall(PlannerToolCatalog.SearchCnvRegulationName)
            ]);
        var plannerAgent = CreatePlanDrivenPlanner(
            plan,
            new FakeDataAgent(CreateDataResult(hasAnomaly: true)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: true)),
            executor,
            new FakeToolExecutionResultMapper(
                CreateDataResult(hasAnomaly: false),
                CreateLegalResult(hasComplianceRisk: false)),
            publisher);

        await plannerAgent.RunAsync(
            TestFinancialReport.CreateContext(),
            CancellationToken.None);

        trace.IndexOf("tool_plan_proposed").Should().BeLessThan(
            trace.IndexOf("tool_plan_validated"));
        trace.IndexOf("tool_plan_validated").Should().BeLessThan(
            trace.IndexOf("rejected"));
        trace.IndexOf("rejected").Should().BeLessThan(
            trace.IndexOf("data_started"));
    }

    private static PlannerAgent CreatePlanDrivenPlanner(
        ToolPlan plan,
        FakeDataAgent dataAgent,
        FakeLegalAgent legalAgent,
        IControlledToolExecutor executor,
        IToolExecutionResultMapper mapper,
        IActivityEventPublisher? publisher = null,
        IPlannerReasoningService? reasoning = null,
        IToolExecutionPolicy? policy = null)
    {
        return new PlannerAgent(
            dataAgent,
            legalAgent,
            publisher ?? new FakeActivityEventPublisher(),
            reasoning ?? new FakePlannerReasoningService(),
            new FakeToolPlanProposalService(plan),
            new ToolPlanNormalizer(),
            new ToolPlanValidator(),
            policy ?? new ToolExecutionPolicy(),
            executor,
            mapper,
            CreatePlanDrivenOptions());
    }

    private static PlannerAgent CreatePlannerForExecutionAudit(
        IControlledToolExecutor executor)
    {
        return CreatePlanDrivenPlanner(
            CreateExecutableToolPlan(),
            new FakeDataAgent(CreateDataResult(hasAnomaly: false)),
            new FakeLegalAgent(CreateLegalResult(hasComplianceRisk: false)),
            executor,
            new FakeToolExecutionResultMapper());
    }

    private static async Task<IReadOnlyList<ToolExecutionResult>>
        InvokeBuildExecutionAuditAsync(
            PlannerAgent plannerAgent,
            IReadOnlyList<ToolExecutionPolicyDecision> policyDecisions)
    {
        var method = typeof(PlannerAgent).GetMethod(
            "BuildExecutionAuditAsync",
            System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var task = (Task<IReadOnlyList<ToolExecutionResult>>)method!.Invoke(
            plannerAgent,
            new object?[] { policyDecisions, CancellationToken.None, null })!;

        return await task;
    }

    private static void AssertPolicyReconciliationFailure(
        IReadOnlyList<ToolExecutionPolicyDecision> reconciled,
        IReadOnlyList<ApprovedToolCall> approvedCalls)
    {
        reconciled.Should().HaveCount(approvedCalls.Count);
        for (var index = 0; index < approvedCalls.Count; index++)
        {
            reconciled[index].Call.Should().BeSameAs(approvedCalls[index]);
            reconciled[index].Status.Should().Be(ToolExecutionStatus.Failed);
            reconciled[index].Reason.Should().Be(
                "Tool execution policy returned decisions inconsistent with validation.");
        }
    }

    private static ApprovedToolCall CreateApprovedToolCall(string toolName)
    {
        return new ApprovedToolCall(
            toolName,
            new Dictionary<string, string>(),
            $"Approve {toolName}.");
    }

    private static ToolExecutionResult CreateToolExecutionResult(
        ApprovedToolCall call,
        bool succeeded = true,
        string outputJson = "{}")
    {
        return new ToolExecutionResult(
            ToolName: call.ToolName,
            Status: succeeded
                ? ToolExecutionStatus.Executed
                : ToolExecutionStatus.Failed,
            Succeeded: succeeded,
            Summary: succeeded
                ? $"Executed {call.ToolName}."
                : $"Failed {call.ToolName}.",
            Engine: "Fake Controlled Tool Executor",
            OutputJson: outputJson,
            Error: succeeded ? null : $"Failed {call.ToolName}.");
    }

    public enum DataStagePlanCase
    {
        Missing,
        Rejected,
        PolicyDenied
    }

    public enum LegalStageOutcome
    {
        Missing,
        Rejected,
        PolicyDenied,
        Failed,
        Malformed
    }

    public enum PlanDrivenCancellationPoint
    {
        DataExecutor,
        DataEvent,
        LegalExecutor,
        LegalEvent
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
            resolvedArguments = new Dictionary<string, string>();
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
                    new Dictionary<string, string>()
                )
            ]
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
        private readonly Action<CancellationToken>? _onCall;

        public FakePlannerReasoningService(
            PlannerReasoningResult? result = null,
            Action<CancellationToken>? onCall = null)
        {
            _result = result ?? CreatePlannerReasoningResult();
            _onCall = onCall;
        }

        public bool WasCalled { get; private set; }

        public PlannerReasoningInput? Input { get; private set; }

        public Task<PlannerReasoningResult> GenerateReasoningAsync(
            PlannerReasoningInput input,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            Input = input;
            _onCall?.Invoke(cancellationToken);

            return Task.FromResult(_result);
        }
    }

    private sealed class FakeToolPlanProposalService : IToolPlanProposalService
    {
        private readonly ToolPlan _result;
        private readonly Action<CancellationToken>? _onCall;
        public ToolPlanProposalInput? Input { get; private set; }

        public FakeToolPlanProposalService(
            ToolPlan? result = null,
            Action<CancellationToken>? onCall = null)
        {
            _result = result ?? new ToolPlan([]);
            _onCall = onCall;
        }

        public Task<ToolPlan> ProposeAsync(
            ToolPlanProposalInput input,
            CancellationToken cancellationToken)
        {
            Input = input;
            _onCall?.Invoke(cancellationToken);
            return Task.FromResult(_result);
        }
    }

    private sealed class TrackingToolPlanNormalizer : IToolPlanNormalizer
    {
        public bool WasCalled { get; private set; }

        public ToolPlan Normalize(ToolPlan plan)
        {
            WasCalled = true;
            return new ToolPlanNormalizer().Normalize(plan);
        }
    }

    private sealed class TrackingToolPlanValidator : IToolPlanValidator
    {
        public bool WasCalled { get; private set; }

        public ToolValidationResult Validate(ToolPlan plan)
        {
            WasCalled = true;
            return new ToolPlanValidator().Validate(plan);
        }
    }

    private sealed class FakeControlledToolExecutor : IControlledToolExecutor
    {
        private readonly Func<
            IReadOnlyList<ApprovedToolCall>,
            PlannerToolExecutionContext?,
            CancellationToken,
            Task<IReadOnlyList<ToolExecutionResult>>>? _handler;
        private readonly List<string>? _trace;

        public FakeControlledToolExecutor(
            Func<
                IReadOnlyList<ApprovedToolCall>,
                PlannerToolExecutionContext?,
                CancellationToken,
                Task<IReadOnlyList<ToolExecutionResult>>>? handler = null,
            List<string>? trace = null)
        {
            _handler = handler;
            _trace = trace;
        }

        public bool WasCalled { get; private set; }

        public IReadOnlyList<ApprovedToolCall> ReceivedCalls { get; private set; } = [];

        public List<ControlledToolInvocation> Invocations { get; } = [];

        public async Task<IReadOnlyList<ToolExecutionResult>> ExecuteAsync(
            IReadOnlyList<ApprovedToolCall> calls,
            CancellationToken cancellationToken,
            PlannerToolExecutionContext? runtimeContext = null)
        {
            WasCalled = true;
            ReceivedCalls = calls;
            Invocations.Add(new ControlledToolInvocation(
                calls.ToArray(),
                runtimeContext));
            var stage = PlannerToolCatalog.Find(calls[0].ToolName)?.SatisfactionKind ==
                PlannerToolSatisfactionKind.DataAnalysis
                ? "data"
                : "legal";
            _trace?.Add($"{stage}_started");

            var results = _handler is null
                ? calls.Select(call => CreateToolExecutionResult(call)).ToArray()
                : await _handler(calls, runtimeContext, cancellationToken);

            _trace?.Add($"{stage}_completed");
            return results;
        }
    }

    private sealed record ControlledToolInvocation(
        IReadOnlyList<ApprovedToolCall> Calls,
        PlannerToolExecutionContext? RuntimeContext);

    private sealed class DenyingToolExecutionPolicy(
        params string[] deniedTools) : IToolExecutionPolicy
    {
        private readonly HashSet<string> _deniedTools = new(
            deniedTools,
            StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<ToolExecutionPolicyDecision> Decide(
            IReadOnlyList<ApprovedToolCall> approvedCalls,
            ToolExecutionPolicyContext context)
        {
            return approvedCalls.Select(call => _deniedTools.Contains(call.ToolName)
                ? new ToolExecutionPolicyDecision(
                    call,
                    ToolExecutionStatus.Failed,
                    "Denied by test policy.")
                : new ToolExecutionPolicyDecision(
                    call,
                    ToolExecutionStatus.Executed,
                    "Approved by test policy."))
                .ToArray();
        }
    }

    private sealed class CallbackToolExecutionPolicy(
        Func<
            IReadOnlyList<ApprovedToolCall>,
            IReadOnlyList<ToolExecutionPolicyDecision>> callback) : IToolExecutionPolicy
    {
        public bool WasCalled { get; private set; }

        public IReadOnlyList<ToolExecutionPolicyDecision> Decide(
            IReadOnlyList<ApprovedToolCall> approvedCalls,
            ToolExecutionPolicyContext context)
        {
            WasCalled = true;
            return callback(approvedCalls);
        }
    }

    private sealed class CallbackActivityEventPublisher(
        Action<ActivityEvent>? onPublish = null) : IActivityEventPublisher
    {
        public List<ActivityEvent> PublishedEvents { get; } = [];

        public Task PublishAsync(
            ActivityEvent activityEvent,
            CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(activityEvent);
            onPublish?.Invoke(activityEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeToolExecutionResultMapper : IToolExecutionResultMapper
    {
        private readonly DataAgentResult? _dataResult;
        private readonly LegalAgentResult? _legalResult;
        private readonly Exception? _dataException;
        private readonly Exception? _legalException;

        public FakeToolExecutionResultMapper(
            DataAgentResult? dataResult = null,
            LegalAgentResult? legalResult = null,
            Exception? dataException = null,
            Exception? legalException = null)
        {
            _dataResult = dataResult;
            _legalResult = legalResult;
            _dataException = dataException;
            _legalException = legalException;
        }

        public List<IReadOnlyList<ToolExecutionResult>> DataAudits { get; } = [];

        public List<IReadOnlyList<ToolExecutionResult>> LegalAudits { get; } = [];

        public DataAgentResult? TryMapDataResult(
            IReadOnlyList<ToolExecutionResult> executedCalls)
        {
            DataAudits.Add(executedCalls.ToArray());
            if (_dataException is not null)
            {
                throw _dataException;
            }

            return _dataResult;
        }

        public LegalAgentResult? TryMapLegalResult(
            IReadOnlyList<ToolExecutionResult> executedCalls)
        {
            LegalAudits.Add(executedCalls.ToArray());
            if (_legalException is not null)
            {
                throw _legalException;
            }

            return _legalResult;
        }
    }

    private sealed class MappingControlledToolExecutor : IControlledToolExecutor
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly DataAgentResult _dataResult;
        private readonly LegalAgentResult _legalOutput;

        public bool WasCalled { get; private set; }

        public MappingControlledToolExecutor(
            DataAgentResult dataResult,
            LegalAgentResult legalOutput)
        {
            _dataResult = dataResult;
            _legalOutput = legalOutput;
        }

        public Task<IReadOnlyList<ToolExecutionResult>> ExecuteAsync(
            IReadOnlyList<ApprovedToolCall> calls,
            CancellationToken cancellationToken,
            PlannerToolExecutionContext? runtimeContext = null)
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
