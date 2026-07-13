using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orchestration.Api.Controllers;
using Orchestration.Application.Activity;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;
using Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;
using Orchestration.Application.Agents.Shared;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Legal.AiReview;
using Orchestration.Application.Agents.Legal.Cnv;
using Orchestration.Application.Agents.Planner;
using Orchestration.Application.Agents.Planner.Reasoning;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.AnalysisSessions;
using Orchestration.Application.FinancialAnalysis.Thresholds;
using Orchestration.Domain.Activity;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Infrastructure.Agents.Data;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis;
using Orchestration.Infrastructure.Agents.Legal;
using Orchestration.Infrastructure.Agents.Legal.Regulations;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;
using Orchestration.Infrastructure.Agents.Planner.ToolCalling;
using Orchestration.Infrastructure.Persistence;
using Orchestration.Tests.Agents.Data.FinancialAnalysis;
using Xunit;

namespace Orchestration.Tests.AnalysisSessions;

public sealed class ProductionLikeWorkflowE2ETests
{
    private const string AllowedCnvCitation = "TEST-CNV-E2E-001";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ProductionLikeWorkflow_Should_preserve_context_and_complete_after_human_approval()
    {
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var activityPublisher = new PersistingActivityEventPublisher(dbContext);
        var pythonService = new FakeProductionPythonFinancialAnalysisService();
        var cnvClient = new FakeProductionCnvRegulationMcpClient();
        var controller = CreateController(
            dbContext,
            activityPublisher,
            pythonService,
            cnvClient
        );

        var createResult = await controller.CreateSession(CancellationToken.None);
        createResult.Should().BeOfType<CreatedAtActionResult>();
        var session = await dbContext.AnalysisSessions.SingleAsync();

        var saveResult = await controller.SaveFinancialMetrics(
            session.Id,
            CreateStructuredMetricsInput(),
            CancellationToken.None
        );
        saveResult.Should().BeOfType<OkObjectResult>();

        var preflightResult = await controller.GetStartPreflight(
            session.Id,
            CancellationToken.None
        );
        var preflight = preflightResult.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<AnalysisSessionStartPreflightResult>()
            .Subject;
        preflight.CanStart.Should().BeTrue();
        preflight.Errors.Should().BeEmpty();

        var startResult = await controller.StartSession(
            session.Id,
            CancellationToken.None
        );
        startResult.Should().BeOfType<OkObjectResult>();

        var startedSession = await dbContext.AnalysisSessions
            .SingleAsync(x => x.Id == session.Id);
        startedSession.Status.Should().Be(AnalysisSessionStatus.AwaitingHumanApproval);
        startedSession.CurrentAgent.Should().BeNull();

        using var startedContext = JsonDocument.Parse(startedSession.ContextJson);
        AssertProductionLikeContext(startedContext.RootElement);
        AssertActivityFeed(activityPublisher.PublishedEvents, beforeApproval: true);
        cnvClient.ReceivedRequests.Should().NotBeEmpty();
        cnvClient.ReceivedRequests.Should().Contain(request =>
            request.Query.Contains("liquidez", StringComparison.OrdinalIgnoreCase) ||
            request.Query.Contains("endeudamiento", StringComparison.OrdinalIgnoreCase)
        );

        var approveResult = await controller.ApproveSession(
            session.Id,
            new HumanDecisionDto("Reviewed deterministic production-like E2E evidence."),
            CancellationToken.None
        );
        approveResult.Should().BeOfType<OkObjectResult>();

        var reloadedSession = await dbContext.AnalysisSessions
            .AsNoTracking()
            .SingleAsync(x => x.Id == session.Id);
        reloadedSession.Status.Should().Be(AnalysisSessionStatus.Completed);
        reloadedSession.CurrentAgent.Should().Be("Orchestrator");

        using var reloadedContext = JsonDocument.Parse(reloadedSession.ContextJson);
        AssertProductionLikeContext(reloadedContext.RootElement);
        AssertActivityFeed(activityPublisher.PublishedEvents, beforeApproval: false);
        var persistedEvents = await dbContext.ActivityEvents
            .AsNoTracking()
            .Where(evt => evt.SessionId == session.Id)
            .ToListAsync();
        persistedEvents.Select(evt => evt.Type).Should().Contain("analysis_completed");
        persistedEvents.Count.Should().Be(activityPublisher.PublishedEvents.Count);
    }

    [Fact]
    public async Task ProductionLikeWorkflow_Should_execute_tool_plan_in_plan_driven_mode_and_complete_after_human_approval()
    {
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var activityPublisher = new PersistingActivityEventPublisher(dbContext);
        var cnvClient = new FakeProductionCnvRegulationMcpClient();
        var controller = CreateController(
            dbContext,
            activityPublisher,
            new FakeProductionPythonFinancialAnalysisService(),
            cnvClient,
            ToolCallingExecutionMode.PlanDriven
        );

        var createResult = await controller.CreateSession(CancellationToken.None);
        createResult.Should().BeOfType<CreatedAtActionResult>();
        var session = await dbContext.AnalysisSessions.SingleAsync();

        var saveResult = await controller.SaveFinancialMetrics(
            session.Id,
            CreateStructuredMetricsInput(),
            CancellationToken.None
        );
        saveResult.Should().BeOfType<OkObjectResult>();

        var startResult = await controller.StartSession(
            session.Id,
            CancellationToken.None
        );
        startResult.Should().BeOfType<OkObjectResult>();

        var startedSession = await dbContext.AnalysisSessions
            .SingleAsync(x => x.Id == session.Id);
        startedSession.Status.Should().Be(AnalysisSessionStatus.AwaitingHumanApproval);

        using var startedContext = JsonDocument.Parse(startedSession.ContextJson);
        AssertPlanDrivenContext(startedContext.RootElement);
        AssertPlanDrivenActivityFeed(activityPublisher.PublishedEvents);
        cnvClient.ReceivedRequests.Should().ContainSingle()
            .Which.Query.Should().Be("agentes");

        var approveResult = await controller.ApproveSession(
            session.Id,
            new HumanDecisionDto("Reviewed plan-driven controlled tool execution evidence."),
            CancellationToken.None
        );
        approveResult.Should().BeOfType<OkObjectResult>();

        var reloadedSession = await dbContext.AnalysisSessions
            .AsNoTracking()
            .SingleAsync(x => x.Id == session.Id);
        reloadedSession.Status.Should().Be(AnalysisSessionStatus.Completed);

        using var reloadedContext = JsonDocument.Parse(reloadedSession.ContextJson);
        AssertPlanDrivenContext(reloadedContext.RootElement);
        var persistedEvents = await dbContext.ActivityEvents
            .AsNoTracking()
            .Where(evt => evt.SessionId == session.Id)
            .ToListAsync();
        persistedEvents.Select(evt => evt.Type).Should().Contain("analysis_completed");
        persistedEvents.Count.Should().Be(activityPublisher.PublishedEvents.Count);
    }


    [Fact]
    public async Task ProductionLikeWorkflow_Should_preserve_context_and_activity_after_human_rejection()
    {
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var activityPublisher = new PersistingActivityEventPublisher(dbContext);
        var controller = CreateController(
            dbContext,
            activityPublisher,
            new FakeProductionPythonFinancialAnalysisService(),
            new FakeProductionCnvRegulationMcpClient()
        );

        var createResult = await controller.CreateSession(CancellationToken.None);
        createResult.Should().BeOfType<CreatedAtActionResult>();
        var session = await dbContext.AnalysisSessions.SingleAsync();

        var saveResult = await controller.SaveFinancialMetrics(
            session.Id,
            CreateStructuredMetricsInput(),
            CancellationToken.None
        );
        saveResult.Should().BeOfType<OkObjectResult>();

        var startResult = await controller.StartSession(
            session.Id,
            CancellationToken.None
        );
        startResult.Should().BeOfType<OkObjectResult>();

        var awaitingSession = await dbContext.AnalysisSessions
            .SingleAsync(x => x.Id == session.Id);
        awaitingSession.Status.Should().Be(AnalysisSessionStatus.AwaitingHumanApproval);

        using var awaitingContext = JsonDocument.Parse(awaitingSession.ContextJson);
        AssertProductionLikeContext(awaitingContext.RootElement);

        var rejectResult = await controller.RejectSession(
            session.Id,
            new HumanDecisionDto("Rejected after reviewing production-like E2E evidence."),
            CancellationToken.None
        );
        rejectResult.Should().BeOfType<OkObjectResult>();

        var rejectedSession = await dbContext.AnalysisSessions
            .AsNoTracking()
            .SingleAsync(x => x.Id == session.Id);
        rejectedSession.Status.Should().Be(AnalysisSessionStatus.Failed);
        rejectedSession.CurrentAgent.Should().BeNull();
        rejectedSession.FailureReason.Should().Be("Rejected after reviewing production-like E2E evidence.");

        using var rejectedContext = JsonDocument.Parse(rejectedSession.ContextJson);
        AssertProductionLikeContext(rejectedContext.RootElement);
        AssertRejectionActivityFeed(activityPublisher.PublishedEvents);

        var eventCountAfterReject = activityPublisher.PublishedEvents.Count;
        var approveAfterReject = await controller.ApproveSession(
            session.Id,
            new HumanDecisionDto("Invalid second approval attempt."),
            CancellationToken.None
        );
        approveAfterReject.Should().BeOfType<ConflictObjectResult>();

        var afterSecondDecision = await dbContext.AnalysisSessions
            .AsNoTracking()
            .SingleAsync(x => x.Id == session.Id);
        afterSecondDecision.Status.Should().Be(AnalysisSessionStatus.Failed);
        afterSecondDecision.FailureReason.Should().Be("Rejected after reviewing production-like E2E evidence.");
        activityPublisher.PublishedEvents.Count.Should().Be(eventCountAfterReject);

        var persistedEvents = await dbContext.ActivityEvents
            .AsNoTracking()
            .Where(evt => evt.SessionId == session.Id)
            .ToListAsync();
        persistedEvents.Count.Should().Be(activityPublisher.PublishedEvents.Count);
        persistedEvents.Select(evt => evt.Type).Should().Contain("analysis_rejected");
        persistedEvents.Select(evt => evt.Type).Should().NotContain("analysis_completed");
    }

    private static AnalysisSessionsController CreateController(
        OrchestrationDbContext dbContext,
        PersistingActivityEventPublisher activityPublisher,
        IPythonFinancialAnalysisService pythonService,
        ICnvRegulationMcpClient cnvClient,
        ToolCallingExecutionMode executionMode = ToolCallingExecutionMode.Shadow)
    {
        var dataAgentOptions = new DataAgentOptions
        {
            FinancialAnalysisToolsEnabled = true,
            UsePythonFinancialAnalysis = true,
            UseLegacyAnomalyDetectionFallback = false,
            UseFixtureMetricsFallback = false,
            RequireSessionFinancialMetrics = true,
            AiReviewEnabled = true,
            RiskThresholdProfile = "default_oil_and_gas_equity_research"
        };
        var toolCallingOptions = new ToolCallingOptions
        {
            Enabled = true,
            ExecutionMode = executionMode
        };
        var metricsSessionService = CreateMetricsSessionService(dbContext, activityPublisher);
        var metricsProvider = new SessionStructuredFinancialMetricsProvider(metricsSessionService);
        var financialWorkflow = new DataAgentFinancialAnalysisWorkflow(
            metricsProvider,
            pythonService,
            new DeterministicDataAgentAiReviewService(),
            new InMemoryFinancialRiskThresholdProfileProvider(),
            Options.Create(dataAgentOptions),
            activityPublisher,
            NullLogger<DataAgentFinancialAnalysisWorkflow>.Instance
        );
        var dataAgent = new ConfigurableDataAgent(
            new ThrowingLegacyDataAgent(),
            financialWorkflow,
            Options.Create(dataAgentOptions),
            NullLogger<ConfigurableDataAgent>.Instance
        );
        var legalSource = new McpRegulatoryKnowledgeSource(
            cnvClient,
            Options.Create(new CnvRegulationMcpOptions
            {
                Enabled = true,
                Command = "not-used",
                Args = [],
                DefaultLimit = 5
            }),
            NullLogger<McpRegulatoryKnowledgeSource>.Instance,
            new DeterministicLegalAnalysisReviewService(),
            dbContext,
            new FinancialAnalysisLegalCnvQueryStrategy(),
            activityPublisher
        );
        var legalAgent = new SemanticKernelLegalAgent(
            new LegalCompliancePlugin(legalSource)
        );
        var planner = new PlannerAgent(
            dataAgent,
            legalAgent,
            activityPublisher,
            new DeterministicPlannerReasoningService(),
            new DeterministicToolPlanProposalService(toolCallingOptions),
            new ToolPlanNormalizer(),
            new ToolPlanValidator(toolCallingOptions),
            new ToolExecutionPolicy(),
            executionMode == ToolCallingExecutionMode.PlanDriven
                ? new ControlledToolExecutor(
                    dataAgent,
                    cnvClient,
                    NullLogger<ControlledToolExecutor>.Instance
                )
                : new ThrowingControlledToolExecutor(),
            new ToolExecutionResultMapper(),
            toolCallingOptions
        );
        var orchestrator = new AnalysisOrchestratorService(
            dbContext,
            new AnalysisSessionWorkflowService(new AnalysisSessionStateMachine()),
            activityPublisher,
            planner,
            new FinancialReportContextResolver()
        );

        var controller = new AnalysisSessionsController(
            dbContext,
            orchestrator,
            new AnalysisSessionStartPreflightValidator(
                Options.Create(dataAgentOptions),
                new FinancialReportContextResolver()),
            activityPublisher,
            metricsSessionService,
            new StructuredFinancialMetricsCsvParser(),
            new FakeStructuredFinancialMetricsPdfIngestionService(),
            new FakeFinancialMetricsExtractionDraftService(),
            Options.Create(new StructuredFinancialMetricsFileUploadOptions())
        );

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-000000000001"),
            new(ClaimTypes.Name, "admin@ezemartino.com")
        };
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };

        return controller;
    }

    private static StructuredFinancialMetricsSessionService CreateMetricsSessionService(
        OrchestrationDbContext dbContext,
        IActivityEventPublisher activityPublisher)
    {
        return new StructuredFinancialMetricsSessionService(
            dbContext,
            new StructuredFinancialMetricsValidator(),
            new FinancialMetricInputMapper(),
            activityPublisher
        );
    }

    private static StructuredFinancialMetricsInput CreateStructuredMetricsInput()
    {
        return new StructuredFinancialMetricsInput(
            DocumentId: "production-like-json-metrics",
            Company: "Production Like Test Co",
            Currency: "USD",
            Unit: "USD_thousand",
            Metrics:
            [
                Metric("Revenue", "2024A", 1647768m),
                Metric("Gross Profit", "2024A", 924000m),
                Metric("EBITDA", "2024A", 760000m),
                Metric("Current Assets", "2024A", 1203000m),
                Metric("Current Liabilities", "2024A", 1000000m),
                Metric("Cash", "2024A", 250000m),
                Metric("Net Debt", "2024A", 1200000m),
                Metric("Equity", "2024A", 841000m),
                Metric("Revenue", "2025E", 1800000m),
                Metric("Gross Profit", "2025E", 920000m),
                Metric("EBITDA", "2025E", 320000m),
                Metric("Current Assets", "2025E", 950000m),
                Metric("Current Liabilities", "2025E", 1400000m),
                Metric("Cash", "2025E", 150000m),
                Metric("Net Debt", "2025E", 1300000m),
                Metric("Equity", "2025E", 899000m)
            ],
            ReportSummary: TestReportSummary.Input
        );
    }

    private static StructuredFinancialMetricInput Metric(
        string name,
        string period,
        decimal value)
    {
        return new StructuredFinancialMetricInput(
            Name: name,
            Period: period,
            Value: value,
            Unit: "USD_thousand",
            Currency: "USD",
            Source: "production_like_e2e",
            SourcePage: 1,
            Confidence: 0.9m
        );
    }

    private sealed class FakeStructuredFinancialMetricsPdfIngestionService
        : IStructuredFinancialMetricsPdfIngestionService
    {
        public Task<StructuredFinancialMetricsPdfIngestionResult> IngestAsync(
            StructuredFinancialMetricsPdfIngestionRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new StructuredFinancialMetricsPdfIngestionResult(
                FinancialMetricsFileOutcome.Failed,
                SaveResult: null,
                ReviewDraft: null,
                Errors: [],
                Warnings: []
            ));
        }
    }

    private sealed class FakeFinancialMetricsExtractionDraftService
        : IFinancialMetricsExtractionDraftService
    {
        public Task<FinancialMetricsExtractionDraftServiceResult> CreateOrReplaceAsync(
            Guid sessionId,
            Guid userId,
            CreateFinancialMetricsExtractionDraftRequest request,
            CancellationToken cancellationToken) => NotFound();

        public Task<FinancialMetricsExtractionDraftServiceResult> GetPendingAsync(
            Guid sessionId,
            Guid userId,
            CancellationToken cancellationToken) => NotFound();

        public Task<FinancialMetricsExtractionDraftServiceResult> UpdateAsync(
            Guid draftId,
            Guid sessionId,
            Guid userId,
            UpdateFinancialMetricsExtractionDraftRequest request,
            CancellationToken cancellationToken) => NotFound();

        public Task<FinancialMetricsExtractionDraftServiceResult> ConfirmAsync(
            Guid draftId,
            Guid sessionId,
            Guid userId,
            CancellationToken cancellationToken) => NotFound();

        public Task<FinancialMetricsExtractionDraftServiceResult> DiscardAsync(
            Guid draftId,
            Guid sessionId,
            Guid userId,
            CancellationToken cancellationToken) => NotFound();

        private static Task<FinancialMetricsExtractionDraftServiceResult> NotFound()
        {
            return Task.FromResult(
                FinancialMetricsExtractionDraftServiceResult.NotFound("Draft not found.")
            );
        }
    }

    private static void AssertProductionLikeContext(JsonElement root)
    {
        root.TryGetProperty("structuredFinancialMetrics", out var structuredMetrics).Should().BeTrue();
        structuredMetrics.GetProperty("documentId").GetString().Should().Be("production-like-json-metrics");
        structuredMetrics.GetProperty("company").GetString().Should().Be("Production Like Test Co");
        structuredMetrics.GetProperty("metrics").GetArrayLength().Should().BeGreaterThan(1);
        structuredMetrics.GetProperty("provenance").GetProperty("ingestionMethod").GetString().Should().Be("json_paste");

        var financialAnalysis = root.GetProperty("financialAnalysis");
        financialAnalysis.ValueKind.Should().NotBe(JsonValueKind.Null);
        financialAnalysis.GetProperty("documentId").GetString().Should().Be("production-like-json-metrics");
        financialAnalysis.GetProperty("company").GetString().Should().Be("Production Like Test Co");
        financialAnalysis.GetProperty("metricsInputSource").GetString().Should().Be(FinancialMetricsInputSources.SessionContext);
        financialAnalysis.GetProperty("warnings").EnumerateArray()
            .Select(warning => warning.GetString())
            .Should()
            .NotContain("Fixture fallback metrics were used. This mode is intended for development/demo only.");
        financialAnalysis.GetProperty("thresholdProfile").GetString().Should().NotBeNullOrWhiteSpace();
        financialAnalysis.GetProperty("thresholdsUsed").GetArrayLength().Should().BeGreaterThan(0);
        financialAnalysis.GetProperty("aiReview").GetProperty("usedFallback").GetBoolean().Should().BeTrue();

        var riskSignals = financialAnalysis.GetProperty("riskSignals").EnumerateArray().ToArray();
        riskSignals.Should().NotBeEmpty();
        riskSignals.Should().OnlyContain(signal =>
            !string.IsNullOrWhiteSpace(signal.GetProperty("metric").GetString()) &&
            signal.GetProperty("value").ValueKind == JsonValueKind.Number &&
            !string.IsNullOrWhiteSpace(signal.GetProperty("thresholdCode").GetString()) &&
            !string.IsNullOrWhiteSpace(signal.GetProperty("thresholdOperator").GetString()) &&
            signal.GetProperty("thresholdValue").ValueKind == JsonValueKind.Number &&
            !string.IsNullOrWhiteSpace(signal.GetProperty("reason").GetString())
        );

        var anomalyEvidence = root.GetProperty("anomaly").GetProperty("evidence").EnumerateArray().ToArray();
        anomalyEvidence.Select(evidence => evidence.GetProperty("metric").GetString())
            .Should()
            .NotContain(["TransactionAmountZScore", "VelocityScore"]);

        var compliance = root.GetProperty("compliance");
        compliance.GetProperty("riskDetected").GetBoolean().Should().BeTrue();
        GetProperty(compliance.GetProperty("queryStrategy"), "source", "Source")
            .GetString()
            .Should()
            .Be("financial_analysis");
        compliance.GetProperty("evidence").GetArrayLength().Should().BeGreaterThan(0);
        var legalReview = compliance.GetProperty("legalReview");
        legalReview.ValueKind.Should().NotBe(JsonValueKind.Null);
        legalReview.GetProperty("usedFallback").GetBoolean().Should().BeTrue();
        AssertLegalReviewUsesOnlyProvidedCitations(legalReview);
        AssertNoForbiddenLegalLanguage(legalReview);

        var planner = root.GetProperty("planner");
        planner.GetProperty("engine").GetString().Should().Be("Deterministic Planner Reasoning");
        planner.GetProperty("usedFallback").GetBoolean().Should().BeTrue();

        var toolPlan = root.GetProperty("toolPlan");
        toolPlan.GetProperty("proposedCalls").GetArrayLength().Should().BeGreaterThan(0);
        toolPlan.GetProperty("approvedCalls").GetArrayLength().Should().BeGreaterThan(0);
        toolPlan.GetProperty("executedCalls").EnumerateArray()
            .Should()
            .OnlyContain(call => call.GetProperty("status").GetString() == "SkippedAlreadySatisfied");
    }

    private static void AssertPlanDrivenContext(JsonElement root)
    {
        root.TryGetProperty("structuredFinancialMetrics", out var structuredMetrics).Should().BeTrue();
        structuredMetrics.GetProperty("documentId").GetString().Should().Be("production-like-json-metrics");

        var financialAnalysis = root.GetProperty("financialAnalysis");
        financialAnalysis.GetProperty("documentId").GetString().Should().Be("production-like-json-metrics");
        financialAnalysis.GetProperty("metricsInputSource").GetString().Should().Be(FinancialMetricsInputSources.SessionContext);
        financialAnalysis.GetProperty("riskSignals").GetArrayLength().Should().BeGreaterThan(0);

        var anomalyEvidence = root.GetProperty("anomaly").GetProperty("evidence").EnumerateArray().ToArray();
        anomalyEvidence.Select(evidence => evidence.GetProperty("metric").GetString())
            .Should()
            .NotContain(["TransactionAmountZScore", "VelocityScore"]);

        var compliance = root.GetProperty("compliance");
        compliance.GetProperty("riskDetected").GetBoolean().Should().BeTrue();
        compliance.GetProperty("evidence").GetArrayLength().Should().BeGreaterThan(0);

        var toolPlan = root.GetProperty("toolPlan");
        toolPlan.GetProperty("proposedCalls").GetArrayLength().Should().Be(2);
        toolPlan.GetProperty("approvedCalls").GetArrayLength().Should().Be(2);
        toolPlan.GetProperty("rejectedCalls").GetArrayLength().Should().Be(0);
        var executedCalls = toolPlan.GetProperty("executedCalls").EnumerateArray().ToArray();
        executedCalls.Should().HaveCount(2);
        executedCalls.Should().OnlyContain(call =>
            call.GetProperty("status").GetString() == "Executed" &&
            call.GetProperty("succeeded").GetBoolean()
        );
        executedCalls.All(DoesNotHaveOutputJson).Should().BeTrue();
    }

    private static bool DoesNotHaveOutputJson(
        JsonElement call)
    {
        return !call.TryGetProperty("outputJson", out _);
    }

    private static void AssertLegalReviewUsesOnlyProvidedCitations(JsonElement legalReview)
    {
        foreach (var area in legalReview.GetProperty("possibleRegulatoryReviewAreas").EnumerateArray())
        {
            foreach (var citation in area.GetProperty("evidenceCitations").EnumerateArray())
            {
                citation.GetString().Should().Be(AllowedCnvCitation);
            }
        }

        foreach (var reference in legalReview.GetProperty("evidenceReferences").EnumerateArray())
        {
            reference.GetProperty("citation").GetString().Should().Be(AllowedCnvCitation);
        }
    }

    private static JsonElement GetProperty(
        JsonElement element,
        string camelCaseName,
        string pascalCaseName)
    {
        return element.TryGetProperty(camelCaseName, out var camelCase)
            ? camelCase
            : element.GetProperty(pascalCaseName);
    }

    private static void AssertNoForbiddenLegalLanguage(JsonElement legalReview)
    {
        var values = new List<string>
        {
            legalReview.GetProperty("reviewSummary").GetString() ?? ""
        };
        values.AddRange(legalReview.GetProperty("warnings").EnumerateArray().Select(value => value.GetString() ?? ""));
        values.AddRange(legalReview.GetProperty("limitations").EnumerateArray().Select(value => value.GetString() ?? ""));
        values.AddRange(legalReview.GetProperty("possibleRegulatoryReviewAreas").EnumerateArray().SelectMany(area => new[]
        {
            area.GetProperty("title").GetString() ?? "",
            area.GetProperty("description").GetString() ?? ""
        }));

        var text = string.Join(" ", values).ToLowerInvariant();
        foreach (var forbidden in LegalAnalysisAiReviewLanguageRules.ForbiddenLanguage)
        {
            text.Should().NotContain(forbidden.ToLowerInvariant());
        }
    }

    private static void AssertActivityFeed(
        IReadOnlyList<ActivityEvent> events,
        bool beforeApproval)
    {
        var eventTypes = events.Select(evt => evt.Type).ToArray();

        eventTypes.Should().Contain("structured_financial_metrics_attached");
        eventTypes.Should().Contain("state_transition_requested");
        eventTypes.Should().Contain("state_changed");
        eventTypes.Should().Contain("agent_started");
        eventTypes.Should().Contain("legal_cnv_queries_derived");
        eventTypes.Should().Contain("legal_agent_ai_review_completed");
        eventTypes.Should().Contain("planner_reasoning_completed");
        eventTypes.Should().Contain("tool_plan_proposed");
        eventTypes.Should().Contain("tool_plan_validated");
        eventTypes.Should().Contain("tool_call_skipped");
        eventTypes.Should().Contain("human_approval_required");

        if (beforeApproval)
        {
            eventTypes.Should().NotContain("human_decision_received");
            eventTypes.Should().NotContain("analysis_completed");
            return;
        }

        eventTypes.Should().Contain("human_decision_received");
        eventTypes.Should().Contain("analysis_completed");
    }

    private static void AssertRejectionActivityFeed(
        IReadOnlyList<ActivityEvent> events)
    {
        var eventTypes = events.Select(evt => evt.Type).ToArray();

        eventTypes.Should().Contain("structured_financial_metrics_attached");
        eventTypes.Should().Contain("state_transition_requested");
        eventTypes.Should().Contain("state_changed");
        eventTypes.Should().Contain("agent_started");
        eventTypes.Should().Contain("legal_cnv_queries_derived");
        eventTypes.Should().Contain("legal_agent_ai_review_completed");
        eventTypes.Should().Contain("planner_reasoning_completed");
        eventTypes.Should().Contain("human_approval_required");
        eventTypes.Should().Contain("human_decision_received");
        eventTypes.Should().Contain("analysis_rejected");
        eventTypes.Should().NotContain("analysis_completed");
        events.Should().NotContain(evt =>
            evt.Type == "state_changed" &&
            evt.Message.Contains("Completed", StringComparison.OrdinalIgnoreCase)
        );
    }

    private static void AssertPlanDrivenActivityFeed(
        IReadOnlyList<ActivityEvent> events)
    {
        var eventTypes = events.Select(evt => evt.Type).ToArray();

        eventTypes.Should().Contain("structured_financial_metrics_attached");
        eventTypes.Should().Contain("state_transition_requested");
        eventTypes.Should().Contain("state_changed");
        eventTypes.Should().Contain("agent_started");
        eventTypes.Should().Contain("tool_plan_proposed");
        eventTypes.Should().Contain("tool_plan_validated");
        eventTypes.Should().Contain("tool_call_executed");
        eventTypes.Should().Contain("planner_reasoning_completed");
        eventTypes.Should().Contain("human_approval_required");
        eventTypes.Should().NotContain("tool_call_skipped");
        eventTypes.Should().NotContain("tool_execution_fallback_used");
        eventTypes.Should().NotContain("analysis_completed");
    }

    private sealed class PersistingActivityEventPublisher : IActivityEventPublisher
    {
        private readonly OrchestrationDbContext _dbContext;

        public PersistingActivityEventPublisher(OrchestrationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public List<ActivityEvent> PublishedEvents { get; } = [];

        public async Task PublishAsync(
            ActivityEvent activityEvent,
            CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(activityEvent);
            _dbContext.ActivityEvents.Add(ActivityEventLog.Create(
                activityEvent.SessionId,
                activityEvent.Type,
                activityEvent.Agent,
                activityEvent.Message,
                activityEvent.Timestamp
            ));

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class FakeProductionPythonFinancialAnalysisService
        : IPythonFinancialAnalysisService
    {
        public Task<ComputeFinancialRatiosResponse> ComputeFinancialRatiosAsync(
            ComputeFinancialRatiosRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ComputeFinancialRatiosResponse(
                Engine: "Fake Python/Pandas",
                Ratios:
                [
                    new FinancialRatio(
                        Name: "current_ratio",
                        Period: "2025E",
                        Value: 0.67m,
                        Unit: "x",
                        Formula: "current_assets / current_liabilities",
                        Inputs: ["current_assets", "current_liabilities"],
                        Interpretation: "Current ratio is below the configured liquidity threshold."
                    ),
                    new FinancialRatio(
                        Name: "net_debt_to_ebitda",
                        Period: "2025E",
                        Value: 4.06m,
                        Unit: "x",
                        Formula: "net_debt / ebitda",
                        Inputs: ["net_debt", "ebitda"],
                        Interpretation: "Net debt to EBITDA is above the configured leverage threshold."
                    )
                ],
                Warnings: []
            ));
        }

        public Task<ComparePeriodsResponse> ComparePeriodsAsync(
            ComparePeriodsRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ComparePeriodsResponse(
                Engine: "Fake Python/Pandas",
                Comparisons:
                [
                    new FinancialPeriodComparison(
                        MetricName: "ebitda",
                        FromPeriod: "2024A",
                        ToPeriod: "2025E",
                        FromValue: 760000m,
                        ToValue: 320000m,
                        AbsoluteChange: -440000m,
                        PercentageChange: -0.58m,
                        Unit: "USD_thousand",
                        Interpretation: "EBITDA declined materially period over period."
                    )
                ],
                Warnings: []
            ));
        }

        public Task<DetectFinancialRiskSignalsResponse> DetectFinancialRiskSignalsAsync(
            DetectFinancialRiskSignalsRequest request,
            CancellationToken cancellationToken)
        {
            var currentRatioThreshold = FindThreshold(request, "LOW_CURRENT_RATIO");
            var leverageThreshold = FindThreshold(request, "HIGH_NET_DEBT_TO_EBITDA");

            return Task.FromResult(new DetectFinancialRiskSignalsResponse(
                Engine: "Fake Python/Pandas",
                Signals:
                [
                    new FinancialRiskSignal(
                        Name: currentRatioThreshold.Code,
                        Severity: currentRatioThreshold.Severity,
                        Period: "2025E",
                        Summary: "Current ratio crossed a liquidity review threshold.",
                        Evidence:
                        [
                            new RiskEvidenceItem(
                                MetricName: "current_ratio",
                                Period: "2025E",
                                Value: 0.67m,
                                Threshold: currentRatioThreshold.Value,
                                Unit: "x",
                                Interpretation: "current_ratio 0.67 crossed the configured threshold < 1.0."
                            )
                        ],
                        Metric: "current_ratio",
                        Value: 0.67m,
                        ThresholdCode: currentRatioThreshold.Code,
                        ThresholdOperator: currentRatioThreshold.Operator,
                        ThresholdValue: currentRatioThreshold.Value,
                        Reason: "current_ratio 0.67 crossed the configured threshold < 1.0."
                    ),
                    new FinancialRiskSignal(
                        Name: leverageThreshold.Code,
                        Severity: leverageThreshold.Severity,
                        Period: "2025E",
                        Summary: "Net debt to EBITDA crossed a leverage review threshold.",
                        Evidence:
                        [
                            new RiskEvidenceItem(
                                MetricName: "net_debt_to_ebitda",
                                Period: "2025E",
                                Value: 4.06m,
                                Threshold: leverageThreshold.Value,
                                Unit: "x",
                                Interpretation: "net_debt_to_ebitda 4.06 crossed the configured threshold >= 3.0."
                            )
                        ],
                        Metric: "net_debt_to_ebitda",
                        Value: 4.06m,
                        ThresholdCode: leverageThreshold.Code,
                        ThresholdOperator: leverageThreshold.Operator,
                        ThresholdValue: leverageThreshold.Value,
                        Reason: "net_debt_to_ebitda 4.06 crossed the configured threshold >= 3.0."
                    )
                ],
                Result: new FinancialAnalysisToolResult(
                    HasRiskSignals: true,
                    RiskLevel: "High",
                    Summary: "Two financial risk signals were detected.",
                    Engine: "Fake Python/Pandas",
                    Evidence: [],
                    Warnings: []
                )
            ));
        }

        public Task<SummarizeQuantitativeEvidenceResponse> SummarizeQuantitativeEvidenceAsync(
            SummarizeQuantitativeEvidenceRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new SummarizeQuantitativeEvidenceResponse(
                Engine: "Fake Python/Pandas",
                Narrative: "High-severity quantitative evidence was identified from session structured metrics.",
                Result: new FinancialAnalysisToolResult(
                    HasRiskSignals: true,
                    RiskLevel: "High",
                    Summary: "High-severity quantitative evidence was summarized.",
                    Engine: "Fake Python/Pandas",
                    Evidence:
                    [
                        new RiskEvidenceItem(
                            MetricName: "net_debt_to_ebitda",
                            Period: "2025E",
                            Value: 4.06m,
                            Threshold: 3.0m,
                            Unit: "x",
                            Interpretation: "Leverage is above the production-like review threshold."
                        )
                    ],
                    Warnings: []
                )
            ));
        }

        private static FinancialRiskThreshold FindThreshold(
            DetectFinancialRiskSignalsRequest request,
            string code)
        {
            return (request.Thresholds ?? []).First(threshold =>
                string.Equals(threshold.Code, code, StringComparison.OrdinalIgnoreCase)
            );
        }
    }

    private sealed class FakeProductionCnvRegulationMcpClient : ICnvRegulationMcpClient
    {
        public List<CnvRegulationSearchRequest> ReceivedRequests { get; } = [];

        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken)
        {
            ReceivedRequests.Add(request);

            return Task.FromResult(new CnvRegulationSearchResponse(
                Query: request.Query,
                Results:
                [
                    new CnvRegulationSearchResult(
                        DocumentId: "cnv-e2e-cited",
                        ChunkId: "chunk-1",
                        Title: "Test CNV cited disclosure evidence",
                        Chapter: null,
                        Section: "Test section",
                        Article: "Test article",
                        Source: "CNV test fixture",
                        Url: "https://example.test/cnv/e2e",
                        Snippet: "Test fixture excerpt for cited financial disclosure review.",
                        Score: 0.91,
                        Citations:
                        [
                            new CnvRegulationCitation(
                                Source: "CNV test fixture",
                                DocumentType: "test_fixture",
                                ResolutionNumber: "E2E",
                                Title: AllowedCnvCitation,
                                Chapter: null,
                                Section: "Test section",
                                Article: AllowedCnvCitation,
                                PublicationDate: null,
                                Url: "https://example.test/cnv/e2e",
                                QuotedText: "Cited test fixture evidence for financial disclosure review."
                            )
                        ]
                    ),
                    new CnvRegulationSearchResult(
                        DocumentId: "cnv-e2e-uncited",
                        ChunkId: "chunk-2",
                        Title: "Uncited test result",
                        Chapter: null,
                        Section: null,
                        Article: null,
                        Source: "CNV test fixture",
                        Url: null,
                        Snippet: "Uncited result should not become strong legal support.",
                        Score: 0.5,
                        Citations: []
                    )
                ],
                Warnings: []
            ));
        }
    }

    private sealed class ThrowingLegacyDataAgent : ILegacyDataAgent
    {
        public Task<DataAgentResult> AnalyzeAsync(
            Application.Agents.Shared.FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Legacy DataAgent should not run in production-like E2E.");
        }
    }

    private sealed class ThrowingControlledToolExecutor : IControlledToolExecutor
    {
        public Task<IReadOnlyList<ToolExecutionResult>> ExecuteAsync(
            IReadOnlyList<ApprovedToolCall> calls,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Shadow mode should not execute controlled tool calls.");
        }
    }
}
