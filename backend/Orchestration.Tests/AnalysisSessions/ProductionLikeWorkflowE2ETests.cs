using System;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
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
using Orchestration.Application.Agents.Legal.Regulations;
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
    private const string UncitedCnvTitle = "Uncited test result";
    private const string UncitedCnvSnippet =
        "Uncited result should not become strong legal support.";
    private const string VerifiedOriginalSnippet =
        "Fragmento original del resultado de búsqueda CNV para verificación E2E.";
    private const string UnavailableOriginalSnippet =
        "Fragmento original cuyo contexto canónico no estará disponible.";
    private static readonly string VerifiedCanonicalDocumentText =
        "CONTEXTO-CANONICO-DOCUMENTO-" + new string('D', 96);
    private static readonly string VerifiedCanonicalArticleText =
        "CONTEXTO-CANONICO-ARTICULO-" + new string('A', 72);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ProductionLikeWorkflow_DegradedFinancialAnalysis_ShouldPersistReviewStateAcrossApiReload()
    {
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var activityPublisher = new PersistingActivityEventPublisher(dbContext);
        var controller = CreateController(
            dbContext,
            activityPublisher,
            new FakeProductionPythonFinancialAnalysisService(degradedRatios: true),
            new FakeProductionCnvRegulationMcpClient(noEvidence: true));

        await controller.CreateSession(CancellationToken.None);
        var session = await dbContext.AnalysisSessions.SingleAsync();
        await controller.SaveFinancialMetrics(
            session.Id,
            CreateStructuredMetricsInput(),
            CancellationToken.None);

        var startResult = await controller.StartSession(session.Id, CancellationToken.None);

        startResult.Should().BeOfType<OkObjectResult>();
        dbContext.ChangeTracker.Clear();
        var reloadedSession = await dbContext.AnalysisSessions
            .AsNoTracking()
            .SingleAsync(item => item.Id == session.Id);
        reloadedSession.Status.Should().Be(AnalysisSessionStatus.AwaitingHumanApproval);
        using (var persistedContext = JsonDocument.Parse(reloadedSession.ContextJson))
        {
            AssertDegradedExecutionContext(persistedContext.RootElement);
        }

        var getResult = await controller.GetSession(session.Id, CancellationToken.None);
        var payload = getResult.Should().BeOfType<OkObjectResult>().Which.Value;
        using var apiDocument = JsonDocument.Parse(JsonSerializer.Serialize(payload, JsonOptions));
        var apiContextJson = apiDocument.RootElement.GetProperty("contextJson").GetString();
        apiContextJson.Should().NotBeNullOrWhiteSpace();
        using var apiContext = JsonDocument.Parse(apiContextJson!);
        AssertDegradedExecutionContext(apiContext.RootElement);
    }

    [Fact]
    public async Task ProductionLikeWorkflow_Should_preserve_context_and_complete_after_human_approval()
    {
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var activityPublisher = new PersistingActivityEventPublisher(dbContext);
        var pythonService = new FakeProductionPythonFinancialAnalysisService();
        var cnvClient = new FakeProductionCnvRegulationMcpClient();
        var dataInvocationCount = 0;
        var legalInvocationCount = 0;
        PlannerAgentResult? plannerResult = null;
        var controller = CreateController(
            dbContext,
            activityPublisher,
            pythonService,
            cnvClient,
            dataReportObserver: _ => dataInvocationCount++,
            legalReportObserver: _ => legalInvocationCount++,
            plannerResultObserver: result => plannerResult = result
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
        plannerResult.Should().NotBeNull();
        plannerResult!.RequiresHumanApproval.Should().BeTrue();

        using var startedContext = JsonDocument.Parse(startedSession.ContextJson);
        AssertProductionLikeContext(startedContext.RootElement);
        AssertActivityFeed(activityPublisher.PublishedEvents, beforeApproval: true);
        dataInvocationCount.Should().Be(1);
        legalInvocationCount.Should().Be(1);
        cnvClient.ReceivedRequests.Should().HaveCountGreaterThanOrEqualTo(1);
        cnvClient.ReceivedRequests.Should().HaveCountLessThanOrEqualTo(4);
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
        PlannerAgentResult? plannerResult = null;
        var controller = CreateController(
            dbContext,
            activityPublisher,
            new FakeProductionPythonFinancialAnalysisService(),
            cnvClient,
            ToolCallingExecutionMode.PlanDriven,
            plannerResultObserver: result => plannerResult = result
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
        plannerResult.Should().NotBeNull();
        plannerResult!.RequiresHumanApproval.Should().BeTrue();

        using var startedContext = JsonDocument.Parse(startedSession.ContextJson);
        AssertPlanDrivenContext(startedContext.RootElement);
        AssertPlanDrivenActivityFeed(activityPublisher.PublishedEvents);
        cnvClient.ReceivedRequests.Should().HaveCountGreaterThanOrEqualTo(1);
        cnvClient.ReceivedRequests.Should().HaveCountLessThanOrEqualTo(4);
        cnvClient.ReceivedRequests.Should().Contain(request =>
            request.Query.Contains("liquidez", StringComparison.OrdinalIgnoreCase));
        cnvClient.ReceivedRequests.Should().NotContain(request =>
            request.Query == "agentes");
        startedSession.ContextJson.Should().Contain("\"source\":\"contextual\"");
        startedSession.ContextJson.Should().NotContain("outputJson");
        startedSession.ContextJson.Should().NotContain("rawPrompt");
        startedSession.ContextJson.Should().NotContain("modelResponse");
        AssertPersistedContextualLegalQueryAudit(
            startedContext.RootElement,
            cnvClient.ReceivedRequests);

        var preApprovalActivityTypes = await dbContext.ActivityEvents
            .AsNoTracking()
            .Where(evt => evt.SessionId == session.Id)
            .OrderBy(evt => evt.Timestamp)
            .Select(evt => evt.Type)
            .ToListAsync();
        preApprovalActivityTypes.Should().ContainInOrder(
            "tool_plan_proposed",
            "tool_plan_validated",
            "tool_call_executed",
            "legal_cnv_queries_derived",
            "tool_call_executed",
            "planner_reasoning_completed",
            "agent_completed");

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
        AssertPersistedContextualLegalQueryAudit(
            reloadedContext.RootElement,
            cnvClient.ReceivedRequests);
        var persistedEvents = await dbContext.ActivityEvents
            .AsNoTracking()
            .Where(evt => evt.SessionId == session.Id)
            .ToListAsync();
        persistedEvents.Select(evt => evt.Type).Should().Contain("analysis_completed");
        persistedEvents.Count.Should().Be(activityPublisher.PublishedEvents.Count);
    }

    [Fact]
    public async Task ProductionLikeWorkflow_CnvEnrichment_ShouldPersistBoundedCanonicalContextAndUnchangedLegalPolicy()
    {
        const int documentLimit = 48;
        const int articleLimit = 36;

        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var activityPublisher = new PersistingActivityEventPublisher(dbContext);
        var cnvClient = new FakeProductionCnvRegulationMcpClient(
            canonicalOutcome: CnvCanonicalFixtureOutcome.PartialAndUnavailable);
        PlannerAgentResult? plannerResult = null;
        var controller = CreateController(
            dbContext,
            activityPublisher,
            new FakeProductionPythonFinancialAnalysisService(),
            cnvClient,
            plannerResultObserver: result => plannerResult = result,
            maxEnrichedHits: 2,
            maxDocumentContextCharacters: documentLimit,
            maxArticleContextCharacters: articleLimit);

        await controller.CreateSession(CancellationToken.None);
        var session = await dbContext.AnalysisSessions.SingleAsync();
        await controller.SaveFinancialMetrics(
            session.Id,
            CreateStructuredMetricsInput(),
            CancellationToken.None);

        var startResult = await controller.StartSession(session.Id, CancellationToken.None);

        startResult.Should().BeOfType<OkObjectResult>();
        plannerResult.Should().NotBeNull();
        var legalResult = plannerResult!.LegalResult;
        legalResult.HasComplianceRisk.Should().BeFalse();
        legalResult.RiskLevel.Should().Be("NotEstablished");
        legalResult.RequiresHumanReview.Should().BeTrue();
        legalResult.EvidenceAssessment.Should().NotBeNull();
        legalResult.EvidenceAssessment!.Applicability.Should().Be("NotEstablished");
        legalResult.EvidenceAssessment.Relevance.Should().Be("Strong");
        legalResult.EvidenceAssessment.Severity.Should().Be("Warning");
        legalResult.EvidenceAssessment.RequiresHumanReview.Should().BeTrue();

        legalResult.EvidenceEnrichments.Should().NotBeNull();
        var enrichments = legalResult.EvidenceEnrichments!;
        enrichments.Select(item => item.Status).Should().Equal(
            RegulatoryEvidenceEnrichmentStatuses.Partial,
            RegulatoryEvidenceEnrichmentStatuses.Unavailable);
        var partial = enrichments[0];
        partial.Original.Snippet.Should().Be(VerifiedOriginalSnippet);
        partial.Document.Should().NotBeNull();
        partial.Document!.Text.Should().Be(VerifiedCanonicalDocumentText[..documentLimit]);
        partial.Document.OriginalTextLength.Should().Be(VerifiedCanonicalDocumentText.Length);
        partial.Document.IsTruncated.Should().BeTrue();
        partial.Article.Should().BeNull();
        partial.Limitations.Should().Contain(
            "El contexto canónico del documento CNV fue truncado por límites de tamaño.",
            "No se pudo verificar el contexto canónico del artículo CNV porque la consulta agotó el tiempo de espera.");

        var unavailable = enrichments[1];
        unavailable.Original.Snippet.Should().Be(UnavailableOriginalSnippet);
        unavailable.Document.Should().BeNull();
        unavailable.Article.Should().BeNull();
        unavailable.Limitations.Should().Contain(
            "No se pudo verificar el contexto canónico del documento CNV porque no fue encontrado.",
            "No se pudo verificar el contexto canónico del artículo CNV porque no fue encontrado.");

        var firstRetrieval = cnvClient.Operations.FindIndex(operation =>
            operation.StartsWith("document:", StringComparison.Ordinal) ||
            operation.StartsWith("article:", StringComparison.Ordinal));
        var lastSearchCompletion = cnvClient.Operations.FindLastIndex(operation =>
            operation.StartsWith("search:completed:", StringComparison.Ordinal));
        firstRetrieval.Should().BeGreaterThan(lastSearchCompletion);
        cnvClient.Operations.Count(operation =>
            operation.StartsWith("document:", StringComparison.Ordinal)).Should().Be(2);
        cnvClient.Operations.Count(operation =>
            operation.StartsWith("article:", StringComparison.Ordinal)).Should().Be(2);

        dbContext.ChangeTracker.Clear();
        var storedSession = await dbContext.AnalysisSessions
            .AsNoTracking()
            .SingleAsync(item => item.Id == session.Id);
        using var storedContext = JsonDocument.Parse(storedSession.ContextJson);
        var compliance = storedContext.RootElement.GetProperty("compliance");
        compliance.GetProperty("riskDetected").GetBoolean().Should().BeFalse();
        compliance.GetProperty("riskLevel").GetString().Should().Be("NotEstablished");
        compliance.GetProperty("requiresHumanReview").GetBoolean().Should().BeTrue();
        compliance.GetProperty("evidenceAssessment")
            .GetProperty("applicability").GetString().Should().Be("NotEstablished");

        var persistedEnrichments = compliance.GetProperty("evidenceEnrichments")
            .EnumerateArray()
            .ToArray();
        persistedEnrichments.Should().HaveCount(2);
        var persistedPartial = persistedEnrichments[0];
        persistedPartial.GetProperty("status").GetString()
            .Should().Be(RegulatoryEvidenceEnrichmentStatuses.Partial);
        persistedPartial.GetProperty("original").GetProperty("snippet")
            .GetString().Should().Be(VerifiedOriginalSnippet);
        persistedPartial.GetProperty("document").GetProperty("text")
            .GetString().Should().Be(VerifiedCanonicalDocumentText[..documentLimit]);
        persistedPartial.GetProperty("document").GetProperty("originalTextLength")
            .GetInt32().Should().Be(VerifiedCanonicalDocumentText.Length);
        persistedPartial.GetProperty("document").GetProperty("isTruncated")
            .GetBoolean().Should().BeTrue();
        persistedPartial.GetProperty("article").ValueKind.Should().Be(JsonValueKind.Null);
        persistedPartial.GetProperty("original").GetProperty("snippet").GetString()
            .Should().NotBe(persistedPartial.GetProperty("document").GetProperty("text").GetString());

        var persistedUnavailable = persistedEnrichments[1];
        persistedUnavailable.GetProperty("status").GetString()
            .Should().Be(RegulatoryEvidenceEnrichmentStatuses.Unavailable);
        persistedUnavailable.GetProperty("limitations").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(
                "No se pudo verificar el contexto canónico del documento CNV porque no fue encontrado.",
                "No se pudo verificar el contexto canónico del artículo CNV porque no fue encontrado.");

        var queryStrategy = compliance.GetProperty("queryStrategy");
        var enrichmentAudits = queryStrategy.GetProperty("enrichments")
            .EnumerateArray()
            .ToArray();
        enrichmentAudits.Should().HaveCount(2);
        enrichmentAudits[0].GetProperty("rank").GetInt32().Should().Be(1);
        enrichmentAudits[0].GetProperty("contributingQueryIndices")
            .GetArrayLength().Should().Be(cnvClient.ReceivedRequests.Count);
        enrichmentAudits[0].GetProperty("document").GetProperty("selected")
            .GetBoolean().Should().BeTrue();
        enrichmentAudits[0].GetProperty("document").GetProperty("attempted")
            .GetBoolean().Should().BeTrue();
        enrichmentAudits[0].GetProperty("document").GetProperty("fromCache")
            .GetBoolean().Should().BeFalse();
        enrichmentAudits[0].GetProperty("document").GetProperty("status")
            .GetString().Should().Be(LegalCnvEnrichmentStageStatuses.Succeeded);
        enrichmentAudits[0].GetProperty("document").GetProperty("originalTextLength")
            .GetInt32().Should().Be(VerifiedCanonicalDocumentText.Length);
        enrichmentAudits[0].GetProperty("document").GetProperty("storedTextLength")
            .GetInt32().Should().Be(documentLimit);
        enrichmentAudits[0].GetProperty("document").GetProperty("isTruncated")
            .GetBoolean().Should().BeTrue();
        enrichmentAudits[0].GetProperty("article").GetProperty("status")
            .GetString().Should().Be(LegalCnvEnrichmentStageStatuses.TimedOut);
        enrichmentAudits[0].GetProperty("article").GetProperty("selected")
            .GetBoolean().Should().BeTrue();
        enrichmentAudits[0].GetProperty("article").GetProperty("attempted")
            .GetBoolean().Should().BeTrue();
        enrichmentAudits[0].GetProperty("article").GetProperty("fromCache")
            .GetBoolean().Should().BeFalse();
        enrichmentAudits[0].GetProperty("article").GetProperty("originalTextLength")
            .ValueKind.Should().Be(JsonValueKind.Null);
        enrichmentAudits[0].GetProperty("article").GetProperty("storedTextLength")
            .ValueKind.Should().Be(JsonValueKind.Null);
        enrichmentAudits[0].GetProperty("article").GetProperty("isTruncated")
            .GetBoolean().Should().BeFalse();
        enrichmentAudits[0].GetProperty("limitationCodes").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain("document_truncated", "article_timed_out");
        enrichmentAudits[1].GetProperty("document").GetProperty("status")
            .GetString().Should().Be(LegalCnvEnrichmentStageStatuses.Missing);
        enrichmentAudits[1].GetProperty("document").GetProperty("attempted")
            .GetBoolean().Should().BeTrue();
        enrichmentAudits[1].GetProperty("document").GetProperty("fromCache")
            .GetBoolean().Should().BeFalse();
        enrichmentAudits[1].GetProperty("article").GetProperty("status")
            .GetString().Should().Be(LegalCnvEnrichmentStageStatuses.Missing);
        enrichmentAudits[1].GetProperty("article").GetProperty("attempted")
            .GetBoolean().Should().BeTrue();
        enrichmentAudits[1].GetProperty("article").GetProperty("fromCache")
            .GetBoolean().Should().BeFalse();
        enrichmentAudits[1].GetProperty("limitationCodes").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain("document_missing", "article_missing");
        var queryStrategyJson = queryStrategy.GetRawText();
        queryStrategyJson.Should().NotContain(VerifiedOriginalSnippet);
        queryStrategyJson.Should().NotContain(VerifiedCanonicalDocumentText[..documentLimit]);
        queryStrategyJson.Should().NotContain("https://");

        activityPublisher.PublishedEvents.Should().ContainSingle(activity =>
            activity.Type == "legal_cnv_enrichment_completed");
    }

    [Fact]
    public async Task ProductionLikeWorkflow_PlanDrivenMultiCallCnvEnrichment_ShouldAggregateDeterministicallyInEitherResultOrder()
    {
        const int documentLimit = 48;
        const int articleLimit = 36;

        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var activityPublisher = new PersistingActivityEventPublisher(dbContext);
        var cnvClient = new FakeProductionCnvRegulationMcpClient(
            canonicalOutcome: CnvCanonicalFixtureOutcome.VerifiedAndUnavailable);
        PlannerAgentResult? plannerResult = null;
        FinancialReportContext? legalReport = null;
        ILegalAgent? legalAgent = null;
        var controller = CreateController(
            dbContext,
            activityPublisher,
            new FakeProductionPythonFinancialAnalysisService(),
            cnvClient,
            ToolCallingExecutionMode.PlanDriven,
            legalReportObserver: report => legalReport = report,
            plannerResultObserver: result => plannerResult = result,
            legalAgentObserver: agent => legalAgent = agent,
            maxEnrichedHits: 2,
            maxDocumentContextCharacters: documentLimit,
            maxArticleContextCharacters: articleLimit);

        await controller.CreateSession(CancellationToken.None);
        var session = await dbContext.AnalysisSessions.SingleAsync();
        await controller.SaveFinancialMetrics(
            session.Id,
            CreateStructuredMetricsInput(),
            CancellationToken.None);

        var startResult = await controller.StartSession(session.Id, CancellationToken.None);

        startResult.Should().BeOfType<OkObjectResult>();
        plannerResult.Should().NotBeNull();
        legalReport.Should().NotBeNull();
        legalAgent.Should().NotBeNull();
        var persistedLegalResult = plannerResult!.LegalResult;
        persistedLegalResult.HasComplianceRisk.Should().BeFalse();
        persistedLegalResult.RiskLevel.Should().Be("NotEstablished");
        persistedLegalResult.RequiresHumanReview.Should().BeTrue();
        persistedLegalResult.EvidenceAssessment!.Applicability.Should().Be("NotEstablished");
        persistedLegalResult.EvidenceAssessment.Severity.Should().Be("Warning");
        persistedLegalResult.EvidenceEnrichments.Should().NotBeNull();
        persistedLegalResult.EvidenceEnrichments!.Select(item => item.Status).Should().Equal(
            RegulatoryEvidenceEnrichmentStatuses.Verified,
            RegulatoryEvidenceEnrichmentStatuses.Unavailable);

        dbContext.ChangeTracker.Clear();
        var storedSession = await dbContext.AnalysisSessions
            .AsNoTracking()
            .SingleAsync(item => item.Id == session.Id);
        using (var storedContext = JsonDocument.Parse(storedSession.ContextJson))
        {
            var storedVerified = storedContext.RootElement
                .GetProperty("compliance")
                .GetProperty("evidenceEnrichments")[0];
            storedVerified.GetProperty("original").GetProperty("snippet")
                .GetString().Should().Be(VerifiedOriginalSnippet);
            storedVerified.GetProperty("document").GetProperty("text")
                .GetString().Should().Be(VerifiedCanonicalDocumentText[..documentLimit]);
            storedVerified.GetProperty("document").GetProperty("originalTextLength")
                .GetInt32().Should().Be(VerifiedCanonicalDocumentText.Length);
            storedVerified.GetProperty("document").GetProperty("isTruncated")
                .GetBoolean().Should().BeTrue();
            storedVerified.GetProperty("article").GetProperty("text")
                .GetString().Should().Be(VerifiedCanonicalArticleText[..articleLimit]);
            storedVerified.GetProperty("article").GetProperty("originalTextLength")
                .GetInt32().Should().Be(VerifiedCanonicalArticleText.Length);
            storedVerified.GetProperty("article").GetProperty("isTruncated")
                .GetBoolean().Should().BeTrue();
        }

        var runtimeContext = new PlannerToolExecutionContext(
            legalReport!,
            plannerResult.DataResult,
            LegalDataEvidenceClassifier.Classify(
                LegalDataToolStatuses.Executed,
                plannerResult.DataResult.FinancialAnalysis));
        var executor = new ControlledToolExecutor(
            new NeverCalledDataAgent(),
            legalAgent!,
            NullLogger<ControlledToolExecutor>.Instance);
        var legalCall = new ApprovedToolCall(
            PlannerToolCatalog.SearchCnvRegulationName,
            new Dictionary<string, string>(),
            "Verificar agregación determinista de dos revisiones legales.");

        cnvClient.CanonicalOutcome = CnvCanonicalFixtureOutcome.PartialAndUnavailable;
        cnvClient.ReverseSearchResults = false;
        var firstCall = await executor.ExecuteAsync(
            [legalCall],
            CancellationToken.None,
            runtimeContext);
        cnvClient.CanonicalOutcome = CnvCanonicalFixtureOutcome.VerifiedAndUnavailable;
        cnvClient.ReverseSearchResults = true;
        var secondCall = await executor.ExecuteAsync(
            [legalCall],
            CancellationToken.None,
            runtimeContext);
        firstCall.Should().ContainSingle(item => item.Succeeded);
        secondCall.Should().ContainSingle(item => item.Succeeded);

        var mapper = new ToolExecutionResultMapper();
        var firstOnly = mapper.TryMapLegalResult(firstCall);
        var secondOnly = mapper.TryMapLegalResult(secondCall);
        firstOnly!.EvidenceEnrichments!.Select(item => item.Status).Should().Equal(
            RegulatoryEvidenceEnrichmentStatuses.Partial,
            RegulatoryEvidenceEnrichmentStatuses.Unavailable);
        secondOnly!.EvidenceEnrichments!.Select(item => item.Status).Should().Equal(
            RegulatoryEvidenceEnrichmentStatuses.Verified,
            RegulatoryEvidenceEnrichmentStatuses.Unavailable);
        var forward = mapper.TryMapLegalResult(firstCall.Concat(secondCall).ToArray());
        var reversed = mapper.TryMapLegalResult(secondCall.Concat(firstCall).ToArray());

        forward.Should().NotBeNull();
        reversed.Should().NotBeNull();
        forward!.HasComplianceRisk.Should().BeFalse();
        forward.RiskLevel.Should().Be("NotEstablished");
        forward.RequiresHumanReview.Should().BeTrue();
        forward.EvidenceAssessment!.Applicability.Should().Be("NotEstablished");
        forward.EvidenceAssessment.Severity.Should().Be("Warning");
        forward.EvidenceEnrichments.Should().BeEquivalentTo(
            reversed!.EvidenceEnrichments,
            options => options.WithStrictOrdering());
        ((LegalQueryStrategyAudit)forward.QueryStrategy!).Enrichments.Should().BeEquivalentTo(
            ((LegalQueryStrategyAudit)reversed.QueryStrategy!).Enrichments,
            options => options.WithStrictOrdering());
        forward.EvidenceEnrichments!.Select(item => item.Status).Should().Equal(
            RegulatoryEvidenceEnrichmentStatuses.Verified,
            RegulatoryEvidenceEnrichmentStatuses.Unavailable);
        forward.EvidenceEnrichments[0].Article.Should().NotBeNull();
        var forwardArticle = forward.EvidenceEnrichments[0].Article!;
        forwardArticle.Text
            .Should().Be(VerifiedCanonicalArticleText[..articleLimit]);
        forward.EvidenceEnrichments[0].Original.Snippet
            .Should().NotBe(forwardArticle.Text);
    }

    [Fact]
    public async Task ProductionLikeWorkflow_PlanDrivenWithoutUsableSignals_ShouldUseOneDurableGenericLegalFallback()
    {
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var activityPublisher = new PersistingActivityEventPublisher(dbContext);
        var cnvClient = new FakeProductionCnvRegulationMcpClient();
        var controller = CreateController(
            dbContext,
            activityPublisher,
            new FakeProductionPythonFinancialAnalysisService(noUsableSignals: true),
            cnvClient,
            ToolCallingExecutionMode.PlanDriven);

        await controller.CreateSession(CancellationToken.None);
        var session = await dbContext.AnalysisSessions.SingleAsync();
        await controller.SaveFinancialMetrics(
            session.Id,
            CreateStructuredMetricsInput(),
            CancellationToken.None);

        var startResult = await controller.StartSession(session.Id, CancellationToken.None);

        startResult.Should().BeOfType<OkObjectResult>();
        cnvClient.ReceivedRequests.Should().ContainSingle()
            .Which.Query.Should().Be("régimen informativo estados financieros emisoras");

        var persistedSession = await dbContext.AnalysisSessions
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == session.Id);
        using var context = JsonDocument.Parse(persistedSession.ContextJson);
        var queryStrategy = context.RootElement
            .GetProperty("compliance")
            .GetProperty("queryStrategy");
        GetProperty(queryStrategy, "source", "Source").GetString()
            .Should().Be("fallback");
        GetProperty(queryStrategy, "fallbackReason", "FallbackReason").GetString()
            .Should().Be(LegalCnvFallbackReasons.NoSpecificSignals);
    }

    [Fact]
    public async Task ProductionLikeWorkflow_PlanDrivenHostileLegalFirstProposal_ShouldExecuteDataBeforeLegal()
    {
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var activityPublisher = new PersistingActivityEventPublisher(dbContext);
        var options = new ToolCallingOptions
        {
            Enabled = true,
            ExecutionMode = ToolCallingExecutionMode.PlanDriven
        };
        var controller = CreateController(
            dbContext,
            activityPublisher,
            new FakeProductionPythonFinancialAnalysisService(),
            new FakeProductionCnvRegulationMcpClient(),
            ToolCallingExecutionMode.PlanDriven,
            new LegalFirstDeterministicToolPlanProposalService(options));

        await controller.CreateSession(CancellationToken.None);
        var session = await dbContext.AnalysisSessions.SingleAsync();
        await controller.SaveFinancialMetrics(
            session.Id,
            CreateStructuredMetricsInput(),
            CancellationToken.None);

        var startResult = await controller.StartSession(session.Id, CancellationToken.None);

        startResult.Should().BeOfType<OkObjectResult>();
        var persistedSession = await dbContext.AnalysisSessions
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == session.Id);
        using var context = JsonDocument.Parse(persistedSession.ContextJson);
        var toolPlan = context.RootElement.GetProperty("toolPlan");
        toolPlan.GetProperty("proposedCalls").EnumerateArray().First()
            .GetProperty("toolName").GetString()
            .Should().Be(PlannerToolCatalog.SearchCnvRegulationName);
        var executedCallNames = toolPlan.GetProperty("executedCalls").EnumerateArray()
            .Select(call => call.GetProperty("toolName").GetString())
            .ToArray();
        executedCallNames.Should().Equal(
            PlannerToolCatalog.AnalyzeTransactionsName,
            PlannerToolCatalog.SearchCnvRegulationName);
        var persistedToolExecutionAgents = await dbContext.ActivityEvents
            .AsNoTracking()
            .Where(activityEvent =>
                activityEvent.SessionId == session.Id &&
                activityEvent.Type == "tool_call_executed")
            .OrderBy(activityEvent => activityEvent.Timestamp)
            .Select(activityEvent => activityEvent.Agent)
            .ToArrayAsync();
        persistedToolExecutionAgents.Should().Equal("DataAgent", "LegalAgent");
    }

    [Fact]
    public async Task ProductionLikeWorkflow_PlanDrivenCancellationBetweenStages_ShouldNotMutateWorkflowOrRunLegal()
    {
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        using var cancellation = new CancellationTokenSource();
        var activityPublisher = new PersistingActivityEventPublisher(
            dbContext,
            activityEvent =>
            {
                if (activityEvent.Type == "tool_call_executed")
                {
                    cancellation.Cancel();
                }
            });
        var cnvClient = new FakeProductionCnvRegulationMcpClient();
        AnalysisOrchestratorService? orchestrator = null;
        var controller = CreateController(
            dbContext,
            activityPublisher,
            new FakeProductionPythonFinancialAnalysisService(),
            cnvClient,
            ToolCallingExecutionMode.PlanDriven,
            orchestratorObserver: service => orchestrator = service);

        await controller.CreateSession(CancellationToken.None);
        var session = await dbContext.AnalysisSessions.SingleAsync();
        await controller.SaveFinancialMetrics(
            session.Id,
            CreateStructuredMetricsInput(),
            CancellationToken.None);

        await FluentActions.Invoking(() =>
                orchestrator!.StartAnalysisAsync(session.Id, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        dbContext.ChangeTracker.Clear();
        var persistedSession = await dbContext.AnalysisSessions
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == session.Id);
        persistedSession.Status.Should().Be(AnalysisSessionStatus.DataGathering);
        persistedSession.CurrentAgent.Should().Be("PlannerAgent");
        persistedSession.ContextJson.Should().NotContain("\"toolPlan\"");
        cnvClient.ReceivedRequests.Should().BeEmpty();

        var activityTypes = await dbContext.ActivityEvents
            .AsNoTracking()
            .Where(activityEvent => activityEvent.SessionId == session.Id)
            .Select(activityEvent => activityEvent.Type)
            .ToListAsync();
        activityTypes.Should().NotContain("legal_cnv_queries_derived");
        activityTypes.Should().NotContain("planner_reasoning_completed");
        activityTypes.Should().NotContain("agent_completed");
        activityTypes.Should().NotContain("human_approval_required");
        activityTypes.Should().NotContain("analysis_completed");
    }

    [Fact]
    public async Task ProductionLikeWorkflow_LlmPlan_ShouldExecutePersistedSubmittedAt()
    {
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var activityPublisher = new PersistingActivityEventPublisher(dbContext);
        var userId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var session = AnalysisSession.Create(userId);
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var expectedSummary = new FinancialReportSummaryInput(
            "llm-plan-report.pdf",
            333333.33m,
            33,
            new DateTimeOffset(2026, 7, 12, 18, 30, 0, TimeSpan.FromHours(-3)));
        var toolCallingOptions = new ToolCallingOptions
        {
            Enabled = true,
            ExecutionMode = ToolCallingExecutionMode.PlanDriven
        };
        var chatResponse = $$"""
            {
              "proposedCalls": [
                {
                  "toolName": "data.analyze_transactions",
                  "arguments": {
                    "sessionId": "{{session.Id}}",
                    "reportName": "llm-plan-report.pdf",
                    "totalAmount": "333333.33",
                    "transactionCount": "33",
                    "submittedAt": "2030-01-01T00:00:00Z"
                  },
                  "reason": "Analizar las transacciones del reporte persistido."
                },
                {
                  "toolName": "legal.search_cnv_regulation",
                  "arguments": {},
                  "reason": "Recuperar evidencia regulatoria CNV para revisión."
                }
              ]
            }
            """;
        var chatCompletionService = new StaticChatCompletionService(chatResponse);
        var proposalService = new SemanticKernelToolPlanProposalService(
            toolCallingOptions,
            new DeterministicToolPlanProposalService(toolCallingOptions),
            new SemanticKernelToolPlanResponseParser(),
            chatCompletionService,
            NullLogger<SemanticKernelToolPlanProposalService>.Instance);
        FinancialReportContext? executedReport = null;
        var controller = CreateController(
            dbContext,
            activityPublisher,
            new FakeProductionPythonFinancialAnalysisService(),
            new FakeProductionCnvRegulationMcpClient(),
            ToolCallingExecutionMode.PlanDriven,
            proposalService,
            report => executedReport = report);

        var saveResult = await controller.SaveFinancialMetrics(
            session.Id,
            CreateStructuredMetricsInput(reportSummary: expectedSummary),
            CancellationToken.None);
        saveResult.Should().BeOfType<OkObjectResult>();

        var startResult = await controller.StartSession(
            session.Id,
            CancellationToken.None);

        startResult.Should().BeOfType<OkObjectResult>();
        chatCompletionService.InvocationCount.Should().Be(1);
        executedReport.Should().NotBeNull();
        var observedReport = executedReport
            ?? throw new InvalidOperationException("Controlled DataAgent execution was not observed.");
        observedReport.SubmittedAt.Should().Be(expectedSummary.SubmittedAt!.Value);
        observedReport.SubmittedAt.ToString("O", CultureInfo.InvariantCulture)
            .Should().Be("2026-07-12T18:30:00.0000000-03:00");

        var persistedSession = await dbContext.AnalysisSessions
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == session.Id);
        using var persistedContext = JsonDocument.Parse(persistedSession.ContextJson);
        var toolPlan = persistedContext.RootElement.GetProperty("toolPlan");
        toolPlan.GetProperty("approvedCalls").GetArrayLength().Should().Be(2);
        toolPlan.GetProperty("rejectedCalls").GetArrayLength().Should().Be(0);
        var executedCalls = toolPlan.GetProperty("executedCalls")
            .EnumerateArray()
            .ToArray();
        executedCalls.Should().HaveCount(2);
        executedCalls.Should().OnlyContain(call =>
            call.GetProperty("status").GetString() == "Executed" &&
            call.GetProperty("succeeded").GetBoolean());
        var dataCall = toolPlan
            .GetProperty("proposedCalls")
            .EnumerateArray()
            .Single(call => call.GetProperty("toolName").GetString() ==
                PlannerToolCatalog.AnalyzeTransactionsName);
        dataCall.GetProperty("reason").GetString()
            .Should().Be("Analizar las transacciones del reporte persistido.");
        dataCall.GetProperty("arguments")
            .GetProperty("submittedAt")
            .GetString()
            .Should().Be("2026-07-12T18:30:00.0000000-03:00");
        persistedSession.ContextJson.Should().NotContain("2030-01-01");
        activityPublisher.PublishedEvents.Should().NotContain(activityEvent =>
            activityEvent.Type == "tool_execution_fallback_used");
    }

    [Fact]
    public async Task ProductionLikeWorkflow_TwoSessions_ShouldKeepPersistedReportsAndToolInputsIsolated()
    {
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var publisher = new PersistingActivityEventPublisher(dbContext);
        var controller = CreateController(
            dbContext,
            publisher,
            new FakeProductionPythonFinancialAnalysisService(),
            new FakeProductionCnvRegulationMcpClient(),
            ToolCallingExecutionMode.PlanDriven);
        var userId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var first = AnalysisSession.Create(userId);
        var second = AnalysisSession.Create(userId);
        dbContext.AnalysisSessions.AddRange(first, second);
        await dbContext.SaveChangesAsync();
        var firstSummary = new FinancialReportSummaryInput(
            "session-one-report.pdf",
            111111.11m,
            11,
            new DateTimeOffset(2026, 7, 12, 18, 30, 0, TimeSpan.Zero));
        var secondSummary = new FinancialReportSummaryInput(
            "session-two-report.pdf",
            222222.22m,
            22,
            new DateTimeOffset(2026, 7, 13, 9, 45, 0, TimeSpan.Zero));

        await controller.SaveFinancialMetrics(
            first.Id,
            CreateStructuredMetricsInput(
                documentId: "session-one-metrics",
                company: "Session One Co",
                reportSummary: firstSummary,
                revenue: 1111m),
            CancellationToken.None);
        await controller.SaveFinancialMetrics(
            second.Id,
            CreateStructuredMetricsInput(
                documentId: "session-two-metrics",
                company: "Session Two Co",
                reportSummary: secondSummary,
                revenue: 2222m),
            CancellationToken.None);

        await controller.StartSession(first.Id, CancellationToken.None);
        await controller.StartSession(second.Id, CancellationToken.None);
        await controller.ApproveSession(
            first.Id,
            new HumanDecisionDto("Approved isolated session one."),
            CancellationToken.None);
        await controller.ApproveSession(
            second.Id,
            new HumanDecisionDto("Approved isolated session two."),
            CancellationToken.None);

        var sessions = await dbContext.AnalysisSessions
            .AsNoTracking()
            .Where(session => session.Id == first.Id || session.Id == second.Id)
            .ToDictionaryAsync(session => session.Id);
        sessions[first.Id].Status.Should().Be(AnalysisSessionStatus.Completed);
        sessions[second.Id].Status.Should().Be(AnalysisSessionStatus.Completed);
        AssertIsolatedFinalContext(
            sessions[first.Id].ContextJson,
            firstSummary,
            expectedDocumentId: "session-one-metrics",
            expectedRevenue: 1111m,
            otherReportName: "session-two-report.pdf",
            otherDocumentId: "session-two-metrics",
            otherRevenue: 2222m);
        AssertIsolatedFinalContext(
            sessions[second.Id].ContextJson,
            secondSummary,
            expectedDocumentId: "session-two-metrics",
            expectedRevenue: 2222m,
            otherReportName: "session-one-report.pdf",
            otherDocumentId: "session-one-metrics",
            otherRevenue: 1111m);
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
        ToolCallingExecutionMode executionMode = ToolCallingExecutionMode.Shadow,
        IToolPlanProposalService? proposalService = null,
        Action<FinancialReportContext>? dataReportObserver = null,
        Action<FinancialReportContext>? legalReportObserver = null,
        Action<AnalysisOrchestratorService>? orchestratorObserver = null,
        Action<PlannerAgentResult>? plannerResultObserver = null,
        Action<ILegalAgent>? legalAgentObserver = null,
        int maxEnrichedHits = 0,
        int maxDocumentContextCharacters = 12_000,
        int maxArticleContextCharacters = 6_000)
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
        IDataAgent dataAgent = new ConfigurableDataAgent(
            new ThrowingLegacyDataAgent(),
            financialWorkflow,
            Options.Create(dataAgentOptions),
            NullLogger<ConfigurableDataAgent>.Instance
        );
        if (dataReportObserver is not null)
        {
            dataAgent = new ObservingDataAgent(dataAgent, dataReportObserver);
        }
        var cnvOptions = Options.Create(new CnvRegulationMcpOptions
        {
            Enabled = true,
            Command = "not-used",
            Args = [],
            DefaultLimit = 5,
            MaxEnrichedHits = maxEnrichedHits,
            MaxDocumentContextCharacters = maxDocumentContextCharacters,
            MaxArticleContextCharacters = maxArticleContextCharacters
        });
        var legalSource = new McpRegulatoryKnowledgeSource(
            cnvClient,
            cnvOptions,
            new CnvRegulatoryHitEnricher(
                cnvClient,
                cnvOptions,
                NullLogger<CnvRegulatoryHitEnricher>.Instance),
            NullLogger<McpRegulatoryKnowledgeSource>.Instance,
            new DeterministicLegalAnalysisReviewService(),
            dbContext,
            new FinancialAnalysisLegalCnvQueryStrategy(),
            activityPublisher
        );
        ILegalAgent legalAgent = new SemanticKernelLegalAgent(
            new LegalCompliancePlugin(legalSource)
        );
        if (legalReportObserver is not null)
        {
            legalAgent = new ObservingLegalAgent(legalAgent, legalReportObserver);
        }
        legalAgentObserver?.Invoke(legalAgent);
        IPlannerAgent planner = new PlannerAgent(
            dataAgent,
            legalAgent,
            activityPublisher,
            new DeterministicPlannerReasoningService(),
            proposalService ?? new DeterministicToolPlanProposalService(toolCallingOptions),
            new ToolPlanNormalizer(),
            new ToolPlanValidator(toolCallingOptions),
            new ToolExecutionPolicy(),
            executionMode == ToolCallingExecutionMode.PlanDriven
                ? new ControlledToolExecutor(
                    dataAgent,
                    legalAgent,
                    NullLogger<ControlledToolExecutor>.Instance
                )
                : new ThrowingControlledToolExecutor(),
            new ToolExecutionResultMapper(),
            toolCallingOptions
        );
        if (plannerResultObserver is not null)
        {
            planner = new ObservingPlannerAgent(planner, plannerResultObserver);
        }
        var orchestrator = new AnalysisOrchestratorService(
            dbContext,
            new AnalysisSessionWorkflowService(new AnalysisSessionStateMachine()),
            activityPublisher,
            planner,
            new FinancialReportContextResolver()
        );
        orchestratorObserver?.Invoke(orchestrator);

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

    private static StructuredFinancialMetricsInput CreateStructuredMetricsInput(
        string documentId = "production-like-json-metrics",
        string company = "Production Like Test Co",
        FinancialReportSummaryInput? reportSummary = null,
        decimal revenue = 1647768m)
    {
        return new StructuredFinancialMetricsInput(
            DocumentId: documentId,
            Company: company,
            Currency: "USD",
            Unit: "USD_thousand",
            Metrics:
            [
                Metric("Revenue", "2024A", revenue),
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
            ReportSummary: reportSummary ?? TestReportSummary.Input
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
            ConfirmFinancialMetricsExtractionDraftRequest request,
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
        var execution = financialAnalysis.GetProperty("execution");
        execution.GetProperty("overallStatus").GetString().Should().Be("succeeded");
        execution.GetProperty("stages").EnumerateArray().Should().HaveCount(4)
            .And.OnlyContain(stage => stage.GetProperty("status").GetString() == "succeeded");

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
        AssertRetrievedLegalEvidenceAssessment(compliance);
        GetProperty(compliance.GetProperty("queryStrategy"), "source", "Source")
            .GetString()
            .Should()
            .Be("contextual");
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

    private static void AssertDegradedExecutionContext(JsonElement root)
    {
        var anomaly = root.GetProperty("anomaly");
        anomaly.GetProperty("detected").GetBoolean().Should().BeFalse();
        anomaly.GetProperty("assessmentStatus").GetString().Should().Be("inconclusive");
        anomaly.GetProperty("requiresHumanReview").GetBoolean().Should().BeTrue();

        var financialAnalysis = root.GetProperty("financialAnalysis");
        financialAnalysis.GetProperty("riskSignals").GetArrayLength().Should().Be(0);
        var execution = financialAnalysis.GetProperty("execution");
        execution.GetProperty("overallStatus").GetString().Should().Be("degraded");
        var stages = execution.GetProperty("stages").EnumerateArray().ToArray();
        stages.Should().HaveCount(4);
        var ratios = stages.Single(stage =>
            stage.GetProperty("operation").GetString() == FinancialAnalysisOperations.Ratios);
        ratios.GetProperty("status").GetString().Should().Be("failed");
        ratios.GetProperty("failureCode").GetString()
            .Should().Be(FinancialAnalysisFailureCodes.PythonInvocationFailed);
        ratios.GetProperty("durationMilliseconds").GetInt64().Should().BeGreaterThanOrEqualTo(0);
        stages.Single(stage =>
                stage.GetProperty("operation").GetString() == FinancialAnalysisOperations.Signals)
            .GetProperty("status").GetString().Should().Be("succeeded");

        var compliance = root.GetProperty("compliance");
        compliance.GetProperty("riskDetected").GetBoolean().Should().BeFalse();
        compliance.GetProperty("evidence").GetArrayLength().Should().Be(0);
        execution.GetRawText().ToLowerInvariant().Should().NotContain("exception");
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
        AssertRetrievedLegalEvidenceAssessment(compliance);
        compliance.GetProperty("evidence").GetArrayLength().Should().BeGreaterThan(0);
        AssertPersistedComplianceEvidence(compliance);
        GetProperty(compliance.GetProperty("queryStrategy"), "source", "Source")
            .GetString().Should().Be("contextual");
        var legalReview = compliance.GetProperty("legalReview");
        AssertLegalReviewUsesOnlyProvidedCitations(legalReview);
        AssertNoForbiddenLegalLanguage(legalReview);

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
        executedCalls.Select(call => call.GetProperty("toolName").GetString())
            .Should().Equal(
                PlannerToolCatalog.AnalyzeTransactionsName,
                PlannerToolCatalog.SearchCnvRegulationName);
        executedCalls.All(DoesNotHaveOutputJson).Should().BeTrue();
        toolPlan.GetProperty("approvedCalls").EnumerateArray()
            .Count(call => call.GetProperty("toolName").GetString() ==
                PlannerToolCatalog.SearchCnvRegulationName)
            .Should().Be(1);
    }

    private static void AssertPersistedContextualLegalQueryAudit(
        JsonElement root,
        IReadOnlyList<CnvRegulationSearchRequest> receivedRequests)
    {
        var toolPlan = root.GetProperty("toolPlan");
        toolPlan.GetProperty("approvedCalls").EnumerateArray()
            .Count(call => call.GetProperty("toolName").GetString() ==
                PlannerToolCatalog.SearchCnvRegulationName)
            .Should().Be(1);

        var queries = root.GetProperty("compliance")
            .GetProperty("queryStrategy")
            .GetProperty("queries")
            .EnumerateArray()
            .ToArray();
        queries.Should().HaveCountGreaterThanOrEqualTo(1);
        queries.Should().HaveCountLessThanOrEqualTo(4);
        receivedRequests.Should().HaveSameCount(queries);

        for (var index = 0; index < queries.Length; index++)
        {
            var queryAudit = queries[index];
            queryAudit.GetProperty("index").GetInt32().Should().Be(index + 1);
            queryAudit.GetProperty("total").GetInt32().Should().Be(queries.Length);
            queryAudit.GetProperty("query").GetString().Should()
                .Be(receivedRequests[index].Query);
            queryAudit.GetProperty("regulationArea").GetString().Should()
                .Be(receivedRequests[index].Area);
            queryAudit.GetProperty("reason").GetString().Should().NotBeNullOrWhiteSpace();
            queryAudit.GetProperty("relatedFinancialSignals").GetArrayLength()
                .Should().BeGreaterThan(0);
            queryAudit.GetProperty("executionStatus").GetString()
                .Should().Be(LegalCnvQueryExecutionStatuses.Succeeded);
            queryAudit.GetProperty("resultCount").GetInt32().Should().Be(2);
            queryAudit.GetProperty("citedEvidenceCount").GetInt32().Should().Be(1);
        }
    }

    private static void AssertPersistedComplianceEvidence(JsonElement compliance)
    {
        var evidence = compliance.GetProperty("evidence").EnumerateArray().ToArray();
        evidence.Should().NotBeEmpty();
        evidence.Should().Contain(item =>
            item.GetProperty("regulation").GetString() == AllowedCnvCitation &&
            item.GetProperty("finding").GetString() ==
                "Cited test fixture evidence for financial disclosure review.");
        evidence.Should().NotContain(item =>
            item.GetProperty("regulation").GetString() == UncitedCnvTitle ||
            item.GetProperty("finding").GetString() == UncitedCnvSnippet);
    }

    private static void AssertRetrievedLegalEvidenceAssessment(JsonElement compliance)
    {
        compliance.GetProperty("riskDetected").GetBoolean().Should().BeFalse();
        compliance.GetProperty("riskLevel").GetString().Should().Be("NotEstablished");

        var assessment = compliance.GetProperty("evidenceAssessment");
        assessment.GetProperty("evidenceFound").GetBoolean().Should().BeTrue();
        assessment.GetProperty("relevance").GetString().Should().Be("Strong");
        assessment.GetProperty("applicability").GetString().Should().Be("NotEstablished");
        assessment.GetProperty("evidenceQuality").GetString().Should().Be("Strong");
        assessment.GetProperty("severity").GetString().Should().Be("Warning");
        assessment.GetProperty("requiresHumanReview").GetBoolean().Should().BeTrue();
    }

    private static void AssertIsolatedFinalContext(
        string contextJson,
        FinancialReportSummaryInput expectedSummary,
        string expectedDocumentId,
        decimal expectedRevenue,
        string otherReportName,
        string otherDocumentId,
        decimal otherRevenue)
    {
        using var document = JsonDocument.Parse(contextJson);
        var root = document.RootElement;
        var report = root.GetProperty("financialReport");
        report.GetProperty("reportName").GetString()
            .Should().Be(expectedSummary.ReportName);
        report.GetProperty("totalAmount").GetDecimal()
            .Should().Be(expectedSummary.TotalAmount);
        report.GetProperty("transactionCount").GetInt32()
            .Should().Be(expectedSummary.TransactionCount);
        report.GetProperty("submittedAt").GetDateTimeOffset()
            .Should().Be(expectedSummary.SubmittedAt);
        var structuredMetrics = root.GetProperty("structuredFinancialMetrics");
        structuredMetrics.GetProperty("documentId").GetString()
            .Should().Be(expectedDocumentId);
        var metrics = structuredMetrics.GetProperty("metrics")
            .EnumerateArray()
            .ToArray();
        var revenueMetrics = metrics
            .Where(metric =>
                metric.GetProperty("name").GetString() == "revenue" &&
                metric.GetProperty("period").GetString() == "2024A")
            .ToArray();
        revenueMetrics.Should().ContainSingle();
        revenueMetrics.Single().GetProperty("value").GetDecimal()
            .Should().Be(expectedRevenue);
        metrics.Should().NotContain(metric =>
            metric.GetProperty("value").GetDecimal() == otherRevenue);

        var financialAnalysis = root.GetProperty("financialAnalysis");
        financialAnalysis.GetProperty("documentId").GetString()
            .Should().Be(expectedDocumentId);
        financialAnalysis.GetRawText().Should().NotContain(otherDocumentId);

        var dataCall = root.GetProperty("toolPlan")
            .GetProperty("proposedCalls")
            .EnumerateArray()
            .Single(call => call.GetProperty("toolName").GetString() ==
                PlannerToolCatalog.AnalyzeTransactionsName);
        var arguments = dataCall.GetProperty("arguments");
        arguments.GetProperty("reportName").GetString()
            .Should().Be(expectedSummary.ReportName);
        arguments.GetProperty("totalAmount").GetString()
            .Should().Be(expectedSummary.TotalAmount!.Value.ToString(CultureInfo.InvariantCulture));
        arguments.GetProperty("transactionCount").GetString()
            .Should().Be(expectedSummary.TransactionCount!.Value.ToString(CultureInfo.InvariantCulture));
        arguments.GetProperty("submittedAt").GetString()
            .Should().Be(expectedSummary.SubmittedAt!.Value.ToString("O", CultureInfo.InvariantCulture));

        contextJson.Should().NotContain(otherReportName);
        contextJson.Should().NotContain(otherDocumentId);
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
        private readonly Action<ActivityEvent>? _onPublished;

        public PersistingActivityEventPublisher(
            OrchestrationDbContext dbContext,
            Action<ActivityEvent>? onPublished = null)
        {
            _dbContext = dbContext;
            _onPublished = onPublished;
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
            _onPublished?.Invoke(activityEvent);
        }
    }

    private sealed class FakeProductionPythonFinancialAnalysisService
        : IPythonFinancialAnalysisService
    {
        private readonly bool _degradedRatios;
        private readonly bool _noUsableSignals;

        public FakeProductionPythonFinancialAnalysisService(
            bool degradedRatios = false,
            bool noUsableSignals = false)
        {
            _degradedRatios = degradedRatios;
            _noUsableSignals = noUsableSignals;
        }

        public Task<ComputeFinancialRatiosResponse> ComputeFinancialRatiosAsync(
            ComputeFinancialRatiosRequest request,
            CancellationToken cancellationToken)
        {
            var response = new ComputeFinancialRatiosResponse(
                Engine: "Fake Python/Pandas",
                Ratios: _degradedRatios
                    ? []
                    :
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
            )
            {
                Execution = _degradedRatios
                    ? FailedExecution(FinancialAnalysisOperations.Ratios)
                    : SucceededExecution(FinancialAnalysisOperations.Ratios)
            };

            return Task.FromResult(response);
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
            )
            {
                Execution = SucceededExecution(FinancialAnalysisOperations.Comparisons)
            });
        }

        public Task<DetectFinancialRiskSignalsResponse> DetectFinancialRiskSignalsAsync(
            DetectFinancialRiskSignalsRequest request,
            CancellationToken cancellationToken)
        {
            var currentRatioThreshold = FindThreshold(request, "LOW_CURRENT_RATIO");
            var leverageThreshold = FindThreshold(request, "HIGH_NET_DEBT_TO_EBITDA");

            if (_degradedRatios || _noUsableSignals)
            {
                return Task.FromResult(new DetectFinancialRiskSignalsResponse(
                    Engine: "Fake Python/Pandas",
                    Signals: [],
                    Result: new FinancialAnalysisToolResult(
                        HasRiskSignals: false,
                        RiskLevel: "Low",
                        Summary: "No financial risk signals were detected.",
                        Engine: "Fake Python/Pandas",
                        Evidence: [],
                        Warnings: []))
                    {
                        Execution = SucceededExecution(FinancialAnalysisOperations.Signals)
                    });
            }

            return Task.FromResult(new DetectFinancialRiskSignalsResponse(
                Engine: "Fake Python/Pandas",
                Signals:
                [
                    new FinancialRiskSignal(
                        Name: currentRatioThreshold.Code,
                        Severity: "High",
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
            )
            {
                Execution = SucceededExecution(FinancialAnalysisOperations.Signals)
            });
        }

        public Task<SummarizeQuantitativeEvidenceResponse> SummarizeQuantitativeEvidenceAsync(
            SummarizeQuantitativeEvidenceRequest request,
            CancellationToken cancellationToken)
        {
            if (_degradedRatios)
            {
                return Task.FromResult(new SummarizeQuantitativeEvidenceResponse(
                    Engine: "Fake Python/Pandas",
                    Narrative: "No conclusive quantitative summary was produced.",
                    Result: new FinancialAnalysisToolResult(
                        HasRiskSignals: false,
                        RiskLevel: "Low",
                        Summary: "No conclusive quantitative evidence.",
                        Engine: "Fake Python/Pandas",
                        Evidence: [],
                        Warnings: []))
                {
                    Execution = SucceededExecution(FinancialAnalysisOperations.Summary)
                });
            }

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
            )
            {
                Execution = SucceededExecution(FinancialAnalysisOperations.Summary)
            });
        }

        private static FinancialAnalysisStageExecution SucceededExecution(string operation) =>
            new(operation, FinancialAnalysisExecutionStatus.Succeeded, 1);

        private static FinancialAnalysisStageExecution FailedExecution(string operation) =>
            new(
                operation,
                FinancialAnalysisExecutionStatus.Failed,
                1,
                FinancialAnalysisFailureCodes.PythonInvocationFailed);

        private static FinancialRiskThreshold FindThreshold(
            DetectFinancialRiskSignalsRequest request,
            string code)
        {
            return (request.Thresholds ?? []).First(threshold =>
                string.Equals(threshold.Code, code, StringComparison.OrdinalIgnoreCase)
            );
        }
    }

    private enum CnvCanonicalFixtureOutcome
    {
        None,
        PartialAndUnavailable,
        VerifiedAndUnavailable
    }

    private sealed class FakeProductionCnvRegulationMcpClient : ICnvRegulationMcpClient
    {
        private readonly bool _noEvidence;

        public FakeProductionCnvRegulationMcpClient(
            bool noEvidence = false,
            CnvCanonicalFixtureOutcome canonicalOutcome = CnvCanonicalFixtureOutcome.None)
        {
            _noEvidence = noEvidence;
            CanonicalOutcome = canonicalOutcome;
        }

        public List<CnvRegulationSearchRequest> ReceivedRequests { get; } = [];
        public List<string> Operations { get; } = [];
        public CnvCanonicalFixtureOutcome CanonicalOutcome { get; set; }
        public bool ReverseSearchResults { get; set; }

        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;

        public Task<CnvRegulationDocumentResponse> GetDocumentAsync(
            CnvRegulationDocumentRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add($"document:{request.DocumentId}");

            if (CanonicalOutcome == CnvCanonicalFixtureOutcome.None)
            {
                return Task.FromResult(
                    new CnvRegulationDocumentResponse(false, null, [], []));
            }

            if (request.DocumentId == "cnv-e2e-verified")
            {
                return Task.FromResult(new CnvRegulationDocumentResponse(
                    Found: true,
                    Document: new CnvRegulationDocument(
                        Id: "cnv-e2e-verified",
                        Source: "CNV test fixture",
                        DocumentType: "test_fixture",
                        ResolutionNumber: "E2E-VERIFIED",
                        Title: "Documento canónico CNV E2E",
                        PublicationDate: "2026-07-21",
                        EffectiveDate: "2026-07-22",
                        Url: "https://example.test/cnv/canonical/document",
                        Status: "vigente",
                        RequiresReview: true,
                        RetrievedAt: "2026-07-26T12:00:00Z",
                        Metadata: new Dictionary<string, string>
                        {
                            ["fixture"] = "bounded-canonical-context"
                        },
                        Text: VerifiedCanonicalDocumentText),
                    Citations:
                    [
                        CreateCanonicalCitation(
                            article: "Artículo 1",
                            quotedText: "Cita canónica del documento E2E.")
                    ],
                    Warnings: []));
            }

            if (CanonicalOutcome == CnvCanonicalFixtureOutcome.VerifiedAndUnavailable)
            {
                throw new TimeoutException("Deterministic fake CNV document timeout.");
            }

            return Task.FromResult(
                new CnvRegulationDocumentResponse(false, null, [], []));
        }

        public Task<CnvRegulationArticleResponse> GetArticleAsync(
            CnvRegulationArticleRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add($"article:{request.Article}");

            if (CanonicalOutcome == CnvCanonicalFixtureOutcome.None)
            {
                return Task.FromResult(
                    new CnvRegulationArticleResponse(false, null, null, 0, []));
            }

            if (request.Article == "Artículo 1")
            {
                if (CanonicalOutcome == CnvCanonicalFixtureOutcome.PartialAndUnavailable)
                {
                    throw new TimeoutException("Deterministic fake CNV article timeout.");
                }

                return Task.FromResult(new CnvRegulationArticleResponse(
                    Found: true,
                    Text: VerifiedCanonicalArticleText,
                    Citation: CreateCanonicalCitation(
                        article: "Artículo 1",
                        quotedText: "Cita canónica del artículo E2E."),
                    Confidence: 0.97,
                    Warnings: []));
            }

            return Task.FromResult(
                new CnvRegulationArticleResponse(false, null, null, 0, []));
        }

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReceivedRequests.Add(request);
            Operations.Add($"search:started:{request.Query}");

            if (_noEvidence)
            {
                Operations.Add($"search:completed:{request.Query}");
                return Task.FromResult(new CnvRegulationSearchResponse(
                    Query: request.Query,
                    Results: [],
                    Warnings: []));
            }

            if (CanonicalOutcome != CnvCanonicalFixtureOutcome.None)
            {
                IReadOnlyList<CnvRegulationSearchResult> results =
                [
                    CreateCanonicalSearchResult(
                        documentId: "cnv-e2e-verified",
                        chunkId: "chunk-canonical-1",
                        article: "Artículo 1",
                        resolutionNumber: "E2E-VERIFIED",
                        snippet: VerifiedOriginalSnippet,
                        score: 0.91),
                    CreateCanonicalSearchResult(
                        documentId: "cnv-e2e-unavailable",
                        chunkId: "chunk-canonical-2",
                        article: "Artículo 2",
                        resolutionNumber: "E2E-UNAVAILABLE",
                        snippet: UnavailableOriginalSnippet,
                        score: 0.82)
                ];
                if (ReverseSearchResults)
                {
                    results = results.Reverse().ToArray();
                }

                Operations.Add($"search:completed:{request.Query}");
                return Task.FromResult(new CnvRegulationSearchResponse(
                    Query: request.Query,
                    Results: results,
                    Warnings: []));
            }

            var response = new CnvRegulationSearchResponse(
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
                        Title: UncitedCnvTitle,
                        Chapter: null,
                        Section: null,
                        Article: null,
                        Source: "CNV test fixture",
                        Url: null,
                        Snippet: UncitedCnvSnippet,
                        Score: 0.5,
                        Citations: []
                    )
                ],
                Warnings: []
            );
            Operations.Add($"search:completed:{request.Query}");
            return Task.FromResult(response);
        }

        private static CnvRegulationSearchResult CreateCanonicalSearchResult(
            string documentId,
            string chunkId,
            string article,
            string resolutionNumber,
            string snippet,
            double score)
        {
            return new CnvRegulationSearchResult(
                DocumentId: documentId,
                ChunkId: chunkId,
                Title: $"Resultado CNV {resolutionNumber}",
                Chapter: "Capítulo E2E",
                Section: "Sección E2E",
                Article: article,
                Source: "CNV test fixture",
                Url: $"https://example.test/cnv/search/{documentId}",
                Snippet: snippet,
                Score: score,
                Citations:
                [
                    new CnvRegulationCitation(
                        Source: "CNV test fixture",
                        DocumentType: "test_fixture",
                        ResolutionNumber: resolutionNumber,
                        Title: $"Cita original {resolutionNumber}",
                        Chapter: "Capítulo E2E",
                        Section: "Sección E2E",
                        Article: article,
                        PublicationDate: "2026-07-21",
                        Url: $"https://example.test/cnv/search/{documentId}",
                        QuotedText: snippet)
                ]);
        }

        private static CnvRegulationCitation CreateCanonicalCitation(
            string article,
            string quotedText)
        {
            return new CnvRegulationCitation(
                Source: "CNV test fixture",
                DocumentType: "test_fixture",
                ResolutionNumber: "E2E-VERIFIED",
                Title: "Cita canónica E2E-VERIFIED",
                Chapter: "Capítulo E2E",
                Section: "Sección E2E",
                Article: article,
                PublicationDate: "2026-07-21",
                Url: "https://example.test/cnv/canonical/citation",
                QuotedText: quotedText);
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

    private sealed class NeverCalledDataAgent : IDataAgent
    {
        public Task<DataAgentResult> AnalyzeAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(
                "DataAgent should not run during the isolated Legal multi-call proof.");
        }
    }

    private sealed class ObservingDataAgent : IDataAgent
    {
        private readonly IDataAgent _inner;
        private readonly Action<FinancialReportContext> _observer;

        public ObservingDataAgent(
            IDataAgent inner,
            Action<FinancialReportContext> observer)
        {
            _inner = inner;
            _observer = observer;
        }

        public Task<DataAgentResult> AnalyzeAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            _observer(report);

            return _inner.AnalyzeAsync(report, cancellationToken);
        }
    }

    private sealed class ObservingLegalAgent : ILegalAgent
    {
        private readonly ILegalAgent _inner;
        private readonly Action<FinancialReportContext> _observer;

        public ObservingLegalAgent(
            ILegalAgent inner,
            Action<FinancialReportContext> observer)
        {
            _inner = inner;
            _observer = observer;
        }

        public Task<LegalAgentResult> ReviewAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            _observer(report);

            return _inner.ReviewAsync(report, cancellationToken);
        }

        public Task<LegalAgentResult> ReviewAsync(
            FinancialReportContext report,
            LegalReviewContext context,
            CancellationToken cancellationToken)
        {
            _observer(report);

            return _inner.ReviewAsync(report, context, cancellationToken);
        }
    }

    private sealed class ObservingPlannerAgent : IPlannerAgent
    {
        private readonly IPlannerAgent _inner;
        private readonly Action<PlannerAgentResult> _observer;

        public ObservingPlannerAgent(
            IPlannerAgent inner,
            Action<PlannerAgentResult> observer)
        {
            _inner = inner;
            _observer = observer;
        }

        public async Task<PlannerAgentResult> RunAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            var result = await _inner.RunAsync(report, cancellationToken);
            _observer(result);

            return result;
        }
    }

    private sealed class StaticChatCompletionService : IChatCompletionService
    {
        private readonly string _content;

        public StaticChatCompletionService(
            string content)
        {
            _content = content;
        }

        public IReadOnlyDictionary<string, object?> Attributes { get; } =
            new Dictionary<string, object?>();

        public int InvocationCount { get; private set; }

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;

            IReadOnlyList<ChatMessageContent> response =
            [
                new ChatMessageContent(
                    AuthorRole.Assistant,
                    _content
                )
            ];

            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class LegalFirstDeterministicToolPlanProposalService
        : IToolPlanProposalService
    {
        private readonly DeterministicToolPlanProposalService _inner;

        public LegalFirstDeterministicToolPlanProposalService(ToolCallingOptions options)
        {
            _inner = new DeterministicToolPlanProposalService(options);
        }

        public async Task<ToolPlan> ProposeAsync(
            ToolPlanProposalInput input,
            CancellationToken cancellationToken)
        {
            var plan = await _inner.ProposeAsync(input, cancellationToken);
            return plan with { ProposedCalls = plan.ProposedCalls.Reverse().ToArray() };
        }
    }

    private sealed class ThrowingControlledToolExecutor : IControlledToolExecutor
    {
        public Task<IReadOnlyList<ToolExecutionResult>> ExecuteAsync(
            IReadOnlyList<ApprovedToolCall> calls,
            CancellationToken cancellationToken,
            PlannerToolExecutionContext? runtimeContext = null)
        {
            throw new InvalidOperationException("Shadow mode should not execute controlled tool calls.");
        }
    }
}
