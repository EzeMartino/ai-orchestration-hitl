using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Shared;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Legal.Cnv;
using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Application.Persistence;
using Orchestration.Tests.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Activity;
using Orchestration.Infrastructure.Agents.Legal;
using Orchestration.Infrastructure.Agents.Legal.Regulations;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;
using Orchestration.Application.Agents.Legal.AiReview;

namespace Orchestration.Tests.Agents.Legal;

public class McpRegulatoryKnowledgeSourceTests
{
    [Fact]
    public async Task ReviewAsync_Should_retain_irrelevant_citations_without_creating_review_area()
    {
        var source = CreateSource(new SingleResultCnvRegulationMcpClient(
            CreateResult(
                "irrelevant",
                "Irrelevant cited result",
                "Snippet with citations.",
                [CreateCitation()],
                score: 0.20)));

        var result = await source.ReviewAsync(
            CreateReportWithFinancialSignal(),
            CancellationToken.None
        );

        result.SourceEngine.Should().Be("MCP CNV Regulation Server");
        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.RequiresHumanReview.Should().BeTrue();
        result.Summary.Should().Be(
            "Se recuperó evidencia regulatoria, pero no se estableció relevancia ni aplicabilidad para una evaluación de cumplimiento.");
        result.Findings.Should().ContainSingle();
        result.Findings[0].Finding.Should().Be("Texto normativo citado.");
        result.Findings[0].Section.Should().Be("Articulo 1");
        result.Warnings.Should().Contain(
            "Se recuperó evidencia regulatoria, pero no alcanzó el umbral de relevancia para crear un área de revisión."
        );
        result.EvidenceAssessment.Should().NotBeNull();
        result.EvidenceAssessment!.EvidenceFound.Should().BeTrue();
        result.EvidenceAssessment.Relevance.Should().Be("None");
        result.EvidenceAssessment.EvidenceQuality.Should().Be("Strong");
        result.EvidenceAssessment.Severity.Should().Be("Info");
        result.EvidenceAssessment.RequiresHumanReview.Should().BeFalse();
        result.LegalReview.Should().NotBeNull();
        result.LegalReview!.PossibleRegulatoryReviewAreas.Should().BeEmpty();
        result.LegalReview.EvidenceReferences.Should().BeEmpty();
    }

    [Fact]
    public async Task ReviewAsync_Should_map_only_cited_mcp_results_to_regulatory_findings()
    {
        var source = CreateSource(new MixedCitationCnvRegulationMcpClient());

        var result = await source.ReviewAsync(
            CreateReport(),
            CancellationToken.None
        );

        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.Findings.Should().ContainSingle();
        result.Findings[0].Finding.Should().Be("Texto normativo citado.");
        result.Findings.Should().NotContain(x => x.Finding == "Snippet without citations.");
    }

    [Fact]
    public async Task ReviewAsync_Should_ignore_mcp_results_without_citations()
    {
        var source = CreateSource(new UncitedCnvRegulationMcpClient());

        var result = await source.ReviewAsync(
            CreateReport(),
            CancellationToken.None
        );

        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.Findings.Should().BeEmpty();
        result.EvidenceAssessment.Should().NotBeNull();
        result.EvidenceAssessment!.EvidenceFound.Should().BeTrue();
        result.EvidenceAssessment.Relevance.Should().Be("Strong");
        result.EvidenceAssessment.EvidenceQuality.Should().Be("None");
        result.EvidenceAssessment.RequiresHumanReview.Should().BeTrue();
        result.Warnings.Should().Contain("candidate source");
        result.Warnings.Should().Contain(
            "Se recuperó evidencia regulatoria potencialmente relevante. Su aplicabilidad no está determinada y requiere revisión legal humana."
        );
    }

    [Fact]
    public async Task ReviewAsync_Should_include_mcp_response_warnings()
    {
        var source = CreateSource(new WarningCnvRegulationMcpClient());

        var result = await source.ReviewAsync(
            CreateReport(),
            CancellationToken.None
        );

        result.Warnings.Should().Contain("requires review");
        result.Warnings.Should().Contain(
            "Se recuperó evidencia regulatoria potencialmente relevante. Su aplicabilidad no está determinada y requiere revisión legal humana."
        );
    }

    [Fact]
    public async Task ReviewAsync_Should_assess_incomplete_relevant_evidence_as_weak_and_require_review()
    {
        var source = CreateSource(new SingleResultCnvRegulationMcpClient(
            CreateResult(
                "weak",
                "Weak cited result",
                "Snippet with incomplete citation.",
                [CreateCitation(url: null)],
                score: 0.60)));

        var result = await source.ReviewAsync(
            CreateReportWithFinancialSignal(),
            CancellationToken.None);

        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.Findings.Should().ContainSingle();
        result.EvidenceAssessment.Should().NotBeNull();
        result.EvidenceAssessment!.EvidenceFound.Should().BeTrue();
        result.EvidenceAssessment.Relevance.Should().Be("Weak");
        result.EvidenceAssessment.EvidenceQuality.Should().Be("Weak");
        result.EvidenceAssessment.Severity.Should().Be("Warning");
        result.EvidenceAssessment.RequiresHumanReview.Should().BeTrue();
        result.LegalReview.Should().NotBeNull();
        result.LegalReview!.PossibleRegulatoryReviewAreas.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ReviewAsync_Should_assess_complete_relevant_evidence_as_strong_and_require_review()
    {
        var reviewService = new CapturingLegalAnalysisReviewService();
        var source = CreateSource(new SingleResultCnvRegulationMcpClient(
            CreateResult(
                "strong",
                "Strong cited result",
                "Snippet with complete citation.",
                [CreateCitation()],
                score: 0.90)),
            reviewService);

        var result = await source.ReviewAsync(
            CreateReportWithFinancialSignal(),
            CancellationToken.None);

        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.Summary.Should().Be(
            "Se recuperó evidencia regulatoria potencialmente relevante como posible área de revisión. La aplicabilidad no está determinada.");
        result.Findings.Should().ContainSingle();
        result.EvidenceAssessment.Should().NotBeNull();
        result.EvidenceAssessment!.Relevance.Should().Be("Strong");
        result.EvidenceAssessment.EvidenceQuality.Should().Be("Strong");
        result.EvidenceAssessment.Severity.Should().Be("Warning");
        result.EvidenceAssessment.RequiresHumanReview.Should().BeTrue();
        result.LegalReview.Should().NotBeNull();
        result.LegalReview!.PossibleRegulatoryReviewAreas.Should().NotBeEmpty();
        reviewService.LastInput.Should().NotBeNull();
        reviewService.LastInput!.EvidenceAssessment.Should()
            .BeEquivalentTo(result.EvidenceAssessment);
    }

    [Fact]
    public async Task ReviewAsync_Should_assess_empty_retrieval_without_establishing_risk()
    {
        var source = CreateSource(new SingleResultCnvRegulationMcpClient(result: null));

        var result = await source.ReviewAsync(
            CreateReport(),
            CancellationToken.None);

        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.Summary.Should().Be(
            "No se recuperó evidencia regulatoria. La ausencia de resultados no constituye una evaluación de cumplimiento.");
        result.Findings.Should().BeEmpty();
        result.EvidenceAssessment.Should().NotBeNull();
        result.EvidenceAssessment!.EvidenceFound.Should().BeFalse();
        result.EvidenceAssessment.Relevance.Should().Be("None");
        result.EvidenceAssessment.EvidenceQuality.Should().Be("None");
        result.EvidenceAssessment.Severity.Should().Be("Info");
        result.EvidenceAssessment.RequiresHumanReview.Should().BeFalse();
        result.Warnings.Should().Contain(
            "La búsqueda MCP no recuperó evidencia regulatoria; esto no establece ausencia de riesgo ni constituye una conclusión legal.");
    }

    [Fact]
    public async Task ReviewAsync_Should_enrich_top_two_hits_globally_after_all_searches()
    {
        await using var dbContext =
            StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        const string canonicalSecret = "SECRETO-CANONICO-SOLO-RESULTADO";
        var client = new AggregateEnrichmentCnvRegulationMcpClient(
        [
            [
                CreateEnrichmentSearchResult("low-1", 0.70, "Artículo 1"),
                CreateEnrichmentSearchResult("low-2", 0.60, "Artículo 2")
            ],
            [
                CreateEnrichmentSearchResult("top-1", 0.99, "Artículo 3"),
                CreateEnrichmentSearchResult("top-2", 0.95, "Artículo 4")
            ]
        ])
        {
            DocumentHandler = (request, _) => Task.FromResult(
                CreateCanonicalDocumentResponse(request.DocumentId, canonicalSecret)),
            ArticleHandler = (request, _) => request.Article == "Artículo 4"
                ? Task.FromException<CnvRegulationArticleResponse>(new TimeoutException())
                : Task.FromResult(CreateCanonicalArticleResponse(request.Article))
        };
        var reviewService = new TrackingLegalAnalysisReviewService();
        var publisher = new FakeActivityEventPublisher();
        var source = CreateSource(
            client,
            dbContext,
            new StaticLegalCnvQueryStrategy(CreateAuditPlan()),
            publisher,
            reviewService,
            mcpOptions: CreateEnrichmentOptions());

        var result = await source.ReviewAsync(
            CreateReportWithFinancialAnalysis(),
            CancellationToken.None);

        client.CallOrder.Take(2).Should().Equal("search:1", "search:2");
        client.CallOrder.Skip(2).Should().OnlyContain(step =>
            step.StartsWith("document:", StringComparison.Ordinal) ||
            step.StartsWith("article:", StringComparison.Ordinal));
        client.DocumentRequests.Select(request => request.DocumentId)
            .Should().BeEquivalentTo("top-1", "top-2");
        client.DocumentRequests.Should().HaveCount(2);
        client.ArticleRequests.Should().HaveCount(2);
        result.Findings.Should().HaveCount(4);
        reviewService.LastInput.Should().NotBeNull();
        reviewService.LastInput!.CnvEvidence.Should().HaveCount(4);
        reviewService.LastInput.EvidenceEnrichments.Should().HaveCount(2);
        reviewService.LastInput.EvidenceEnrichments.Should().OnlyContain(enrichment =>
            enrichment.Status == RegulatoryEvidenceEnrichmentStatuses.Verified ||
            enrichment.Status == RegulatoryEvidenceEnrichmentStatuses.Partial);
        reviewService.LastInput.EvidenceEnrichments
            .Single(enrichment =>
                enrichment.Status == RegulatoryEvidenceEnrichmentStatuses.Partial)
            .Limitations.Should().Contain(
                "No se pudo verificar el contexto canónico del artículo CNV porque la consulta agotó el tiempo de espera.");
        result.EvidenceEnrichments.Should().HaveCount(2);
        result.EvidenceEnrichments!.Select(enrichment => enrichment.Status)
            .Should().BeEquivalentTo(
                RegulatoryEvidenceEnrichmentStatuses.Verified,
                RegulatoryEvidenceEnrichmentStatuses.Partial);
        result.Warnings.Count(warning =>
                warning == "No se pudo verificar el contexto canónico del artículo CNV porque la consulta agotó el tiempo de espera.")
            .Should().Be(1);
        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.RequiresHumanReview.Should().BeTrue();
        result.EvidenceAssessment!.Applicability.Should().Be("NotEstablished");
        result.EvidenceAssessment.Severity.Should().Be("Warning");

        var audit = result.QueryStrategy.Should()
            .BeOfType<LegalQueryStrategyAudit>().Subject;
        audit.Enrichments.Should().HaveCount(2);
        audit.Enrichments.Should().BeAssignableTo<System.Collections.IList>()
            .Which.IsReadOnly.Should().BeTrue();
        audit.Enrichments!.Single(item =>
                item.Status == RegulatoryEvidenceEnrichmentStatuses.Partial)
            .Article.Status.Should().Be(LegalCnvEnrichmentStageStatuses.TimedOut);
        JsonSerializer.Serialize(audit.Enrichments).Should().NotContain(canonicalSecret);
        publisher.PublishedEvents.Should().ContainSingle(@event =>
            @event.Type == "legal_cnv_enrichment_completed" &&
            @event.Agent == "LegalAgent" &&
            @event.Message ==
                "Verificación de contexto CNV: 2 seleccionados, 1 verificados, 1 parciales, 0 con conflicto y 0 no disponibles.");
    }

    [Fact]
    public async Task ReviewAsync_Should_retain_conflict_for_audit_but_exclude_it_from_review()
    {
        await using var dbContext =
            StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        const string canonicalSecret = "SECRETO-CANONICO-CONFLICTO";
        const string metadataSecret = "SECRETO-METADATA-CONFLICTO";
        var client = new AggregateEnrichmentCnvRegulationMcpClient(
        [[CreateEnrichmentSearchResult("conflict-doc", 0.90, "Artículo 8")]])
        {
            DocumentHandler = (_, _) => Task.FromResult(
                CreateCanonicalDocumentResponse(
                    "otro-documento",
                    canonicalSecret,
                    metadataSecret))
        };
        var reviewService = new TrackingLegalAnalysisReviewService();
        var publisher = new FakeActivityEventPublisher();
        var logger = new CapturingLogger();
        var source = CreateSource(
            client,
            dbContext,
            new StaticLegalCnvQueryStrategy(CreateAuditPlan(queryCount: 1)),
            publisher,
            reviewService,
            logger,
            CreateEnrichmentOptions());

        var result = await source.ReviewAsync(
            CreateReportWithFinancialAnalysis(),
            CancellationToken.None);

        result.Findings.Should().ContainSingle();
        reviewService.LastInput!.CnvEvidence.Should().ContainSingle();
        reviewService.LastInput.EvidenceEnrichments.Should().BeEmpty();
        result.EvidenceEnrichments.Should().ContainSingle()
            .Which.Status.Should().Be(RegulatoryEvidenceEnrichmentStatuses.Conflict);
        result.EvidenceEnrichments![0].Document!.Text.Should().Be(canonicalSecret);
        result.EvidenceEnrichments[0].Document!.Metadata["secret"]
            .Should().Be(metadataSecret);
        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.RequiresHumanReview.Should().BeTrue();
        result.EvidenceAssessment!.Applicability.Should().Be("NotEstablished");
        result.EvidenceAssessment.Severity.Should().Be("Warning");

        var audit = result.QueryStrategy.Should()
            .BeOfType<LegalQueryStrategyAudit>().Subject
            .Enrichments.Should().ContainSingle().Subject;
        audit.Status.Should().Be(RegulatoryEvidenceEnrichmentStatuses.Conflict);
        audit.Document.Status.Should().Be(LegalCnvEnrichmentStageStatuses.Conflict);
        audit.LimitationCodes.Should().Contain("document_conflict");
        JsonSerializer.Serialize(audit).Should().NotContain(canonicalSecret);
        string.Join(" ", logger.Entries.Select(entry => entry.Message)
                .Concat(publisher.PublishedEvents.Select(@event => @event.Message)))
            .Should().NotContain(canonicalSecret)
            .And.NotContain(metadataSecret);
        publisher.PublishedEvents.Should().ContainSingle(@event =>
            @event.Type == "legal_cnv_enrichment_completed" &&
            @event.Message ==
                "Verificación de contexto CNV: 1 seleccionados, 0 verificados, 0 parciales, 1 con conflicto y 0 no disponibles.");
    }

    [Fact]
    public async Task ReviewAsync_Should_retain_unavailable_original_and_require_human_review()
    {
        await using var dbContext =
            StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var client = new AggregateEnrichmentCnvRegulationMcpClient(
        [[CreateEnrichmentSearchResult("missing-doc", 0.90, "Artículo 9")]])
        {
            DocumentHandler = (_, _) => Task.FromResult(
                new CnvRegulationDocumentResponse(false, null, [], [])),
            ArticleHandler = (_, _) => Task.FromResult(
                new CnvRegulationArticleResponse(false, null, null, 0, []))
        };
        var reviewService = new TrackingLegalAnalysisReviewService();
        var source = CreateSource(
            client,
            dbContext,
            new StaticLegalCnvQueryStrategy(CreateAuditPlan(queryCount: 1)),
            reviewService: reviewService,
            mcpOptions: CreateEnrichmentOptions());

        var result = await source.ReviewAsync(
            CreateReportWithFinancialAnalysis(),
            CancellationToken.None);

        result.Findings.Should().ContainSingle();
        reviewService.LastInput!.CnvEvidence.Should().ContainSingle();
        reviewService.LastInput.EvidenceEnrichments.Should().BeEmpty();
        result.EvidenceEnrichments.Should().ContainSingle()
            .Which.Status.Should().Be(RegulatoryEvidenceEnrichmentStatuses.Unavailable);
        result.RequiresHumanReview.Should().BeTrue();
        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.EvidenceAssessment!.Applicability.Should().Be("NotEstablished");
    }

    [Fact]
    public async Task ReviewAsync_Should_retain_enrichment_when_ai_review_is_not_run()
    {
        await using var dbContext =
            StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var client = new AggregateEnrichmentCnvRegulationMcpClient(
        [[CreateEnrichmentSearchResult("no-ai-doc", 0.90, "Artículo 11")]]);
        var reviewService = new TrackingLegalAnalysisReviewService();
        var source = CreateSource(
            client,
            dbContext,
            new StaticLegalCnvQueryStrategy(CreateAuditPlan(queryCount: 1)),
            reviewService: reviewService,
            mcpOptions: CreateEnrichmentOptions());

        var result = await source.ReviewAsync(
            new RegulatoryReviewRequest(
                CreateReport(),
                new LegalReviewContext(
                    FinancialAnalysisResolutionMode.ProvidedOnly)),
            CancellationToken.None);

        reviewService.CallCount.Should().Be(0);
        result.LegalReview!.FailureReason.Should().Be("financial_analysis_missing");
        result.EvidenceEnrichments.Should().ContainSingle()
            .Which.Status.Should().Be(RegulatoryEvidenceEnrichmentStatuses.Verified);
    }

    [Fact]
    public async Task ReviewAsync_CallerCancellationDuringEnrichment_Should_stop_review_and_events()
    {
        await using var dbContext =
            StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        using var cts = new CancellationTokenSource();
        var client = new AggregateEnrichmentCnvRegulationMcpClient(
        [[CreateEnrichmentSearchResult("cancel-doc", 0.90, "Artículo 10")]])
        {
            DocumentHandler = (_, cancellationToken) =>
            {
                cts.Cancel();
                return Task.FromCanceled<CnvRegulationDocumentResponse>(
                    cancellationToken);
            }
        };
        var reviewService = new TrackingLegalAnalysisReviewService();
        var publisher = new FakeActivityEventPublisher();
        var source = CreateSource(
            client,
            dbContext,
            new StaticLegalCnvQueryStrategy(CreateAuditPlan(queryCount: 1)),
            publisher,
            reviewService,
            mcpOptions: CreateEnrichmentOptions());

        Func<Task> action = () => source.ReviewAsync(
            CreateReportWithFinancialAnalysis(),
            cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        reviewService.CallCount.Should().Be(0);
        publisher.PublishedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task ReviewAsync_Should_publish_zero_selection_enrichment_event()
    {
        await using var dbContext =
            StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var publisher = new FakeActivityEventPublisher();
        var source = CreateSource(
            new AggregateEnrichmentCnvRegulationMcpClient([[]]),
            dbContext,
            new StaticLegalCnvQueryStrategy(CreateAuditPlan(queryCount: 1)),
            publisher,
            mcpOptions: CreateEnrichmentOptions());

        await source.ReviewAsync(
            CreateReportWithFinancialAnalysis(),
            CancellationToken.None);

        publisher.PublishedEvents.Should().ContainSingle(@event =>
            @event.Type == "legal_cnv_enrichment_completed" &&
            @event.Message ==
                "Verificación de contexto CNV: 0 seleccionados, 0 verificados, 0 parciales, 0 con conflicto y 0 no disponibles.");
    }

    [Fact]
    public async Task ReviewAsync_EnrichmentPublisherFailure_Should_log_and_continue()
    {
        await using var dbContext =
            StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var logger = new CapturingLogger();
        var publisher = new EnrichmentActivityEventPublisher(throwOnEnrichment: true);
        var reviewService = new TrackingLegalAnalysisReviewService();
        var source = CreateSource(
            new AggregateEnrichmentCnvRegulationMcpClient([[]]),
            dbContext,
            new StaticLegalCnvQueryStrategy(CreateAuditPlan(queryCount: 1)),
            publisher,
            reviewService,
            logger,
            CreateEnrichmentOptions());

        var result = await source.ReviewAsync(
            CreateReportWithFinancialAnalysis(),
            CancellationToken.None);

        result.Should().NotBeNull();
        reviewService.CallCount.Should().Be(1);
        logger.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message ==
                "Failed to publish activity feed event for CNV enrichment.");
    }

    [Fact]
    public async Task ReviewAsync_EnrichmentPublisherCancellation_Should_propagate_before_review()
    {
        await using var dbContext =
            StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var publisher = new EnrichmentActivityEventPublisher(cancelOnEnrichment: true);
        var reviewService = new TrackingLegalAnalysisReviewService();
        var source = CreateSource(
            new AggregateEnrichmentCnvRegulationMcpClient([[]]),
            dbContext,
            new StaticLegalCnvQueryStrategy(CreateAuditPlan(queryCount: 1)),
            publisher,
            reviewService,
            mcpOptions: CreateEnrichmentOptions());

        Func<Task> action = () => source.ReviewAsync(
            CreateReportWithFinancialAnalysis(),
            CancellationToken.None);

        await action.Should().ThrowAsync<OperationCanceledException>();
        reviewService.CallCount.Should().Be(0);
        publisher.PublishedEvents.Should().NotContain(@event =>
            @event.Type == "legal_agent_ai_review_completed");
    }

    private static McpRegulatoryKnowledgeSource CreateSource(
        ICnvRegulationMcpClient client,
        ILegalAnalysisReviewService? reviewService = null)
    {
        var options = Options.Create(
            new CnvRegulationMcpOptions
            {
                Enabled = true,
                Command = "dotnet",
                Args = [],
                DefaultLimit = 5,
                MaxEnrichedHits = 0
            }
        );
        var enricher = new CnvRegulatoryHitEnricher(
            client,
            options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CnvRegulatoryHitEnricher>.Instance);

        return new McpRegulatoryKnowledgeSource(
            client,
            options,
            enricher,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<McpRegulatoryKnowledgeSource>.Instance,
            reviewService ?? new DeterministicLegalAnalysisReviewService()
        );
    }

    private static FinancialReportContext CreateReport()
    {
        return new FinancialReportContext(
            SessionId: Guid.NewGuid(),
            ReportName: "cnv-mcp-test-report",
            TotalAmount: 125000m,
            TransactionCount: 42,
            SubmittedAt: DateTimeOffset.UtcNow
        );
    }

    private static FinancialAnalysisContext CreateFinancialAnalysis(
        params FinancialRiskSignal[] signals)
    {
        return new FinancialAnalysisContext(
            Engine: "TestDataEngine",
            DocumentId: "doc-123",
            Company: "TestCorp",
            Ratios: Array.Empty<FinancialRatio>(),
            Comparisons: Array.Empty<FinancialPeriodComparison>(),
            RiskSignals: signals,
            RiskEvidence: Array.Empty<RiskEvidenceItem>(),
            Warnings: Array.Empty<string>(),
            Limitations: Array.Empty<string>()
        )
        {
            Execution = FinancialAnalysisExecution.FromStages(
                FinancialAnalysisOperations.All.Select(operation =>
                    new FinancialAnalysisStageExecution(
                        operation,
                        FinancialAnalysisExecutionStatus.Succeeded,
                        DurationMilliseconds: 1)))
        };
    }

    private static async Task<AnalysisSession> PersistFinancialAnalysisAsync(
        IOrchestrationDbContext dbContext,
        FinancialAnalysisContext financialAnalysis)
    {
        var session = AnalysisSession.Create(Guid.NewGuid());
        session.SetContext(System.Text.Json.JsonSerializer.Serialize(
            new { financialAnalysis }));

        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        return session;
    }

    private static async Task<(
        LegalCompliancePluginResult Result,
        QueryTrackingCnvRegulationMcpClient Client,
        CapturingLegalCnvQueryStrategy Strategy)> ReviewThroughPluginAsync(
            string? financialAnalysisJson,
            string? dataEvidenceJson)
    {
        await using var dbContext =
            StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var client = new QueryTrackingCnvRegulationMcpClient();
        var strategy = new CapturingLegalCnvQueryStrategy();
        var source = CreateSource(client, dbContext, strategy);
        var persistedAnalysis = CreateFinancialAnalysis(
            new FinancialRiskSignal(
                "liquidity_risk",
                "High",
                "Q1",
                "Low current ratio",
                Array.Empty<RiskEvidenceItem>())
        );
        var session = await PersistFinancialAnalysisAsync(
            dbContext,
            persistedAnalysis
        );
        var plugin = new LegalCompliancePlugin(source);

        var result = await plugin.ReviewFinancialComplianceAsync(
            reportName: "plugin-boundary-test",
            totalAmount: 1000d,
            transactionCount: 1,
            sessionId: session.Id.ToString(),
            financialAnalysisJson: financialAnalysisJson,
            allowPersistedFinancialAnalysisFallback: false,
            dataEvidenceJson: dataEvidenceJson,
            cancellationToken: CancellationToken.None
        );

        return (result, client, strategy);
    }

    private static LegalCnvQueryPlan CreateAuditPlan(
        bool fallback = false,
        int queryCount = 2)
    {
        var financialAnalysis = fallback
            ? null
            : CreateFinancialAnalysis(
                new FinancialRiskSignal(
                    "audit_signal",
                    "Medium",
                    "Q1",
                    "Audit signal.",
                    Array.Empty<RiskEvidenceItem>())
            );
        var dataEvidence = LegalDataEvidenceClassifier.Classify(
            LegalDataToolStatuses.Executed,
            financialAnalysis
        );
        var queries = Enumerable.Range(1, queryCount)
            .Select(index => new LegalCnvQuery(
                $"audit query {index}",
                $"audit_area_{index}",
                $"Audit reason {index}.",
                [$"audit_signal_{index}"]
            ))
            .ToArray();

        return new LegalCnvQueryPlan(
            "audit_strategy_v1",
            fallback
                ? LegalCnvQuerySources.Fallback
                : LegalCnvQuerySources.Contextual,
            fallback ? dataEvidence.FallbackReason : null,
            dataEvidence,
            queries
        );
    }

    private static FinancialReportContext CreateReportWithFinancialAnalysis()
    {
        return CreateReport() with
        {
            FinancialAnalysis = CreateFinancialAnalysis(
                new FinancialRiskSignal(
                    "audit_signal",
                    "Medium",
                    "Q1",
                    "Audit signal.",
                    Array.Empty<RiskEvidenceItem>()))
        };
    }

    private static FinancialReportContext CreateReportWithFinancialSignal()
    {
        var signal = new FinancialRiskSignal(
            "liquidity_risk",
            "High",
            "Q1",
            "Low current ratio",
            []);
        var financialAnalysis = new FinancialAnalysisContext(
            Engine: "TestDataEngine",
            DocumentId: "doc-123",
            Company: "TestCorp",
            Ratios: [],
            Comparisons: [],
            RiskSignals: [signal],
            RiskEvidence: [],
            Warnings: [],
            Limitations: []);

        return CreateReport() with { FinancialAnalysis = financialAnalysis };
    }

    private sealed class SingleResultCnvRegulationMcpClient(
        CnvRegulationSearchResult? result) : ICnvRegulationMcpClient
    {
        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;

        public Task<CnvRegulationDocumentResponse> GetDocumentAsync(
            CnvRegulationDocumentRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationDocumentResponse(false, null, [], []));

        public Task<CnvRegulationArticleResponse> GetArticleAsync(
            CnvRegulationArticleRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationArticleResponse(false, null, null, 0, []));

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<CnvRegulationSearchResult> results = result is null
                ? []
                : [result];

            return Task.FromResult(new CnvRegulationSearchResponse(
                request.Query,
                results,
                []));
        }
    }

    private sealed class CapturingLegalAnalysisReviewService : ILegalAnalysisReviewService
    {
        private readonly DeterministicLegalAnalysisReviewService _inner = new();

        public LegalAnalysisReviewInput? LastInput { get; private set; }

        public Task<LegalAnalysisReviewResult> ReviewAsync(
            LegalAnalysisReviewInput input,
            CancellationToken cancellationToken)
        {
            LastInput = input;
            return _inner.ReviewAsync(input, cancellationToken);
        }
    }

    private sealed class MixedCitationCnvRegulationMcpClient : ICnvRegulationMcpClient
    {
        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;

        public Task<CnvRegulationDocumentResponse> GetDocumentAsync(
            CnvRegulationDocumentRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationDocumentResponse(false, null, [], []));

        public Task<CnvRegulationArticleResponse> GetArticleAsync(
            CnvRegulationArticleRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationArticleResponse(false, null, null, 0, []));

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new CnvRegulationSearchResponse(
                    request.Query,
                    [
                        CreateResult(
                            "uncited",
                            "Uncited result",
                            "Snippet without citations.",
                            []
                        ),
                        CreateResult(
                            "cited",
                            "Cited result",
                            "Snippet with citations.",
                            [
                                CreateCitation()
                            ]
                        )
                    ],
                    []
                )
            );
        }
    }

    private sealed class UncitedCnvRegulationMcpClient : ICnvRegulationMcpClient
    {
        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;

        public Task<CnvRegulationDocumentResponse> GetDocumentAsync(
            CnvRegulationDocumentRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationDocumentResponse(false, null, [], []));

        public Task<CnvRegulationArticleResponse> GetArticleAsync(
            CnvRegulationArticleRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationArticleResponse(false, null, null, 0, []));

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new CnvRegulationSearchResponse(
                    request.Query,
                    [
                        CreateResult(
                            "uncited",
                            "Uncited result",
                            "Snippet without citations.",
                            []
                        )
                    ],
                    ["candidate source"]
                )
            );
        }
    }

    private sealed class WarningCnvRegulationMcpClient : ICnvRegulationMcpClient
    {
        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;

        public Task<CnvRegulationDocumentResponse> GetDocumentAsync(
            CnvRegulationDocumentRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationDocumentResponse(false, null, [], []));

        public Task<CnvRegulationArticleResponse> GetArticleAsync(
            CnvRegulationArticleRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationArticleResponse(false, null, null, 0, []));

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new CnvRegulationSearchResponse(
                    request.Query,
                    [
                        CreateResult(
                            "cited",
                            "Cited result",
                            "Snippet with citations.",
                            [
                                CreateCitation()
                            ]
                        )
                    ],
                    ["requires review"]
                )
            );
        }
    }

    private static CnvRegulationSearchResult CreateResult(
        string documentId,
        string title,
        string snippet,
        IReadOnlyList<CnvRegulationCitation> citations,
        double score = 0.90)
    {
        return new CnvRegulationSearchResult(
            DocumentId: documentId,
            ChunkId: $"{documentId}-chunk",
            Title: title,
            Chapter: "Capitulo I",
            Section: null,
            Article: "Articulo 1",
            Source: "Infoleg",
            Url: "https://servicios.infoleg.gob.ar/",
            Snippet: snippet,
            Score: score,
            Citations: citations
        );
    }

    private static CnvRegulationCitation CreateCitation(
        string? url = "https://servicios.infoleg.gob.ar/")
    {
        return new CnvRegulationCitation(
            Source: "Infoleg",
            DocumentType: "Resolucion General",
            ResolutionNumber: "622/2013",
            Title: "Resolución General 622/2013 - Texto actualizado",
            Chapter: "Capitulo I",
            Section: null,
            Article: "Articulo 1",
            PublicationDate: "2013-09-09",
            Url: url,
            QuotedText: "Texto normativo citado."
        );
    }

    private static CnvRegulationMcpOptions CreateEnrichmentOptions()
    {
        return new CnvRegulationMcpOptions
        {
            Enabled = true,
            Command = "dotnet",
            Args = [],
            DefaultLimit = 5,
            MaxEnrichedHits = 2,
            MaxDocumentContextCharacters = 1_000,
            MaxArticleContextCharacters = 1_000
        };
    }

    private static CnvRegulationSearchResult CreateEnrichmentSearchResult(
        string documentId,
        double score,
        string article)
    {
        var citation = CreateCanonicalCitation(article);
        return new CnvRegulationSearchResult(
            DocumentId: documentId,
            ChunkId: $"chunk-{documentId}",
            Title: citation.Title,
            Chapter: citation.Chapter,
            Section: citation.Section,
            Article: citation.Article,
            Source: citation.Source,
            Url: citation.Url,
            Snippet: $"Fragmento original {documentId}.",
            Score: score,
            Citations: [citation]);
    }

    private static CnvRegulationDocumentResponse CreateCanonicalDocumentResponse(
        string documentId,
        string text = "Contexto documental canónico.",
        string metadataValue = "AR")
    {
        return new CnvRegulationDocumentResponse(
            Found: true,
            Document: new CnvRegulationDocument(
                Id: documentId,
                Source: "CNV",
                DocumentType: "Resolución General",
                ResolutionNumber: "1/2020",
                Title: "Documento canónico",
                PublicationDate: "2020-01-01",
                EffectiveDate: "2020-02-01",
                Url: "https://cnv.example/document",
                Status: "vigente",
                RequiresReview: true,
                RetrievedAt: "2026-07-26T00:00:00Z",
                Metadata: new Dictionary<string, string>
                {
                    ["jurisdiccion"] = "AR",
                    ["secret"] = metadataValue
                },
                Text: text),
            Citations: [CreateCanonicalCitation(article: null)],
            Warnings: []);
    }

    private static CnvRegulationArticleResponse CreateCanonicalArticleResponse(
        string article)
    {
        return new CnvRegulationArticleResponse(
            Found: true,
            Text: "Texto canónico del artículo.",
            Citation: CreateCanonicalCitation(article),
            Confidence: 0.98,
            Warnings: []);
    }

    private static CnvRegulationCitation CreateCanonicalCitation(string? article)
    {
        return new CnvRegulationCitation(
            Source: "CNV",
            DocumentType: "Resolución General",
            ResolutionNumber: "1/2020",
            Title: "Resolución General 1/2020",
            Chapter: "I",
            Section: "1",
            Article: article,
            PublicationDate: "2020-01-01",
            Url: "https://cnv.example/citation",
            QuotedText: "Texto original citado.");
    }

    private sealed class AggregateEnrichmentCnvRegulationMcpClient(
        IReadOnlyList<IReadOnlyList<CnvRegulationSearchResult>> searchResults)
        : ICnvRegulationMcpClient
    {
        private int _searchIndex;

        public Func<
            CnvRegulationDocumentRequest,
            CancellationToken,
            Task<CnvRegulationDocumentResponse>> DocumentHandler { get; init; } =
                (request, _) => Task.FromResult(
                    CreateCanonicalDocumentResponse(request.DocumentId));

        public Func<
            CnvRegulationArticleRequest,
            CancellationToken,
            Task<CnvRegulationArticleResponse>> ArticleHandler { get; init; } =
                (request, _) => Task.FromResult(
                    CreateCanonicalArticleResponse(request.Article));

        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;
        public List<string> CallOrder { get; } = [];
        public List<CnvRegulationDocumentRequest> DocumentRequests { get; } = [];
        public List<CnvRegulationArticleRequest> ArticleRequests { get; } = [];

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken)
        {
            var index = _searchIndex++;
            CallOrder.Add($"search:{index + 1}");
            var results = index < searchResults.Count
                ? searchResults[index]
                : Array.Empty<CnvRegulationSearchResult>();
            return Task.FromResult(new CnvRegulationSearchResponse(
                request.Query,
                results,
                []));
        }

        public Task<CnvRegulationDocumentResponse> GetDocumentAsync(
            CnvRegulationDocumentRequest request,
            CancellationToken cancellationToken)
        {
            DocumentRequests.Add(request);
            CallOrder.Add($"document:{request.DocumentId}");
            return DocumentHandler(request, cancellationToken);
        }

        public Task<CnvRegulationArticleResponse> GetArticleAsync(
            CnvRegulationArticleRequest request,
            CancellationToken cancellationToken)
        {
            ArticleRequests.Add(request);
            CallOrder.Add($"article:{request.Article}");
            return ArticleHandler(request, cancellationToken);
        }
    }

    private sealed class EnrichmentActivityEventPublisher(
        bool throwOnEnrichment = false,
        bool cancelOnEnrichment = false) : IActivityEventPublisher
    {
        public List<ActivityEvent> PublishedEvents { get; } = [];

        public Task PublishAsync(
            ActivityEvent @event,
            CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(@event);
            if (@event.Type == "legal_cnv_enrichment_completed")
            {
                if (cancelOnEnrichment)
                {
                    throw new OperationCanceledException(
                        "Publisher canceled enrichment event.");
                }

                if (throwOnEnrichment)
                {
                    throw new InvalidOperationException(
                        "Publisher failed enrichment event.");
                }
            }

            return Task.CompletedTask;
        }
    }

    // ==========================================
    // Phase 10 Block 10.4 Integration Tests
    // ==========================================

    private static McpRegulatoryKnowledgeSource CreateSource(
        ICnvRegulationMcpClient client,
        IOrchestrationDbContext dbContext,
        ILegalCnvQueryStrategy? queryStrategy = null,
        IActivityEventPublisher? activityPublisher = null,
        ILegalAnalysisReviewService? reviewService = null,
        ILogger<McpRegulatoryKnowledgeSource>? logger = null,
        CnvRegulationMcpOptions? mcpOptions = null)
    {
        var optionsValue = mcpOptions ?? new CnvRegulationMcpOptions
        {
            Enabled = true,
            Command = "dotnet",
            Args = [],
            DefaultLimit = 5,
            MaxEnrichedHits = 0
        };
        var options = Options.Create(
            optionsValue
        );
        var enricher = new CnvRegulatoryHitEnricher(
            client,
            options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CnvRegulatoryHitEnricher>.Instance);

        return new McpRegulatoryKnowledgeSource(
            client,
            options,
            enricher,
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<McpRegulatoryKnowledgeSource>.Instance,
            reviewService ?? new DeterministicLegalAnalysisReviewService(),
            dbContext,
            queryStrategy,
            activityPublisher
        );
    }

    private sealed class QueryTrackingCnvRegulationMcpClient : ICnvRegulationMcpClient
    {
        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;

        public List<CnvRegulationSearchRequest> ReceivedRequests { get; } = [];

        public Task<CnvRegulationDocumentResponse> GetDocumentAsync(
            CnvRegulationDocumentRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationDocumentResponse(false, null, [], []));

        public Task<CnvRegulationArticleResponse> GetArticleAsync(
            CnvRegulationArticleRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationArticleResponse(false, null, null, 0, []));

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken)
        {
            ReceivedRequests.Add(request);

            IReadOnlyList<CnvRegulationSearchResult> results = Array.Empty<CnvRegulationSearchResult>();

            if (request.Query.Contains("liquidez"))
            {
                results = new[]
                {
                    CreateResult("doc-liq", "Liquidez Title", "Snippet for liquidez.", new[]
                    {
                        new CnvRegulationCitation(
                            Source: "Infoleg",
                            DocumentType: "Resolucion General",
                            ResolutionNumber: "622/2013",
                            Title: "Resolución General 622/2013 - Liquidez",
                            Chapter: "Capitulo I",
                            Section: null,
                            Article: "Articulo 1",
                            PublicationDate: "2013-09-09",
                            Url: "https://servicios.infoleg.gob.ar/",
                            QuotedText: "Texto citado de liquidez."
                        )
                    })
                };
            }
            else if (request.Query.Contains("endeudamiento"))
            {
                results = new[]
                {
                    CreateResult("doc-lev", "Leverage Title", "Snippet for leverage.", new[]
                    {
                        new CnvRegulationCitation(
                            Source: "Infoleg",
                            DocumentType: "Resolucion General",
                            ResolutionNumber: "622/2013",
                            Title: "Resolución General 622/2013 - Endeudamiento",
                            Chapter: "Capitulo II",
                            Section: null,
                            Article: "Articulo 2",
                            PublicationDate: "2013-09-09",
                            Url: "https://servicios.infoleg.gob.ar/",
                            QuotedText: "Texto citado de leverage."
                        )
                    })
                };
            }
            else if (request.Query.Contains("resultados"))
            {
                // Uncited result
                results = new[]
                {
                    CreateResult("doc-uncited", "Uncited Title", "Uncited Snippet.", Array.Empty<CnvRegulationCitation>())
                };
            }
            else if (request.Query == "régimen informativo estados financieros emisoras")
            {
                results = new[]
                {
                    CreateResult("doc-fallback", "Fallback Title", "Snippet for fallback.", new[]
                    {
                        new CnvRegulationCitation(
                            Source: "Infoleg",
                            DocumentType: "Resolucion General",
                            ResolutionNumber: "622/2013",
                            Title: "Resolución General 622/2013 - Fallback",
                            Chapter: "Capitulo III",
                            Section: null,
                            Article: "Articulo 3",
                            PublicationDate: "2013-09-09",
                            Url: "https://servicios.infoleg.gob.ar/",
                            QuotedText: "Texto citado de fallback."
                        )
                    })
                };
            }

            return Task.FromResult(new CnvRegulationSearchResponse(request.Query, results, Array.Empty<string>()));
        }
    }

    private sealed class CapturingLegalCnvQueryStrategy : ILegalCnvQueryStrategy
    {
        private readonly FinancialAnalysisLegalCnvQueryStrategy _inner = new();

        public FinancialAnalysisContext? ReceivedFinancialAnalysis { get; private set; }

        public LegalDataEvidenceContext? ReceivedDataEvidence { get; private set; }

        public LegalCnvQueryPlan BuildPlan(
            FinancialAnalysisContext? financialAnalysis,
            LegalDataEvidenceContext dataEvidence)
        {
            ReceivedFinancialAnalysis = financialAnalysis;
            ReceivedDataEvidence = dataEvidence;
            return _inner.BuildPlan(financialAnalysis, dataEvidence);
        }
    }

    private sealed class StaticLegalCnvQueryStrategy(
        LegalCnvQueryPlan plan) : ILegalCnvQueryStrategy
    {
        public LegalCnvQueryPlan Plan { get; } = plan;

        public LegalCnvQueryPlan BuildPlan(
            FinancialAnalysisContext? financialAnalysis,
            LegalDataEvidenceContext dataEvidence)
        {
            return Plan;
        }
    }

    private sealed class PartialFailureCnvRegulationMcpClient : ICnvRegulationMcpClient
    {
        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;
        public int CallCount { get; private set; }

        public Task<CnvRegulationDocumentResponse> GetDocumentAsync(
            CnvRegulationDocumentRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationDocumentResponse(false, null, [], []));

        public Task<CnvRegulationArticleResponse> GetArticleAsync(
            CnvRegulationArticleRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationArticleResponse(false, null, null, 0, []));

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount > 1)
            {
                throw new InvalidOperationException(
                    "sensitive partial failure payload");
            }

            return Task.FromResult(new CnvRegulationSearchResponse(
                request.Query,
                [
                    CreateResult(
                        "partial-success",
                        "Partial success",
                        "Partial cited evidence.",
                        [CreateCitation()])
                ],
                []
            ));
        }
    }

    private sealed class AlwaysFailingCnvRegulationMcpClient : ICnvRegulationMcpClient
    {
        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;

        public Task<CnvRegulationDocumentResponse> GetDocumentAsync(
            CnvRegulationDocumentRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationDocumentResponse(false, null, [], []));

        public Task<CnvRegulationArticleResponse> GetArticleAsync(
            CnvRegulationArticleRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationArticleResponse(false, null, null, 0, []));

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("sensitive total failure payload");
        }
    }

    private sealed class CancelingCnvRegulationMcpClient : ICnvRegulationMcpClient
    {
        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;

        public Task<CnvRegulationDocumentResponse> GetDocumentAsync(
            CnvRegulationDocumentRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationDocumentResponse(false, null, [], []));

        public Task<CnvRegulationArticleResponse> GetArticleAsync(
            CnvRegulationArticleRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationArticleResponse(false, null, null, 0, []));

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken)
        {
            throw new OperationCanceledException("MCP canceled without token state.");
        }
    }

    private sealed class OrderedSuccessCnvRegulationMcpClient(
        List<string> steps) : ICnvRegulationMcpClient
    {
        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;
        public int CallCount { get; private set; }

        public Task<CnvRegulationDocumentResponse> GetDocumentAsync(
            CnvRegulationDocumentRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationDocumentResponse(false, null, [], []));

        public Task<CnvRegulationArticleResponse> GetArticleAsync(
            CnvRegulationArticleRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationArticleResponse(false, null, null, 0, []));

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            steps.Add($"query:{CallCount}");
            return Task.FromResult(new CnvRegulationSearchResponse(
                request.Query,
                [
                    CreateResult(
                        $"ordered-{CallCount}",
                        "Ordered result",
                        "Ordered cited evidence.",
                        [CreateCitation()])
                ],
                []
            ));
        }
    }

    private sealed class TrackingLegalAnalysisReviewService : ILegalAnalysisReviewService
    {
        private readonly DeterministicLegalAnalysisReviewService _inner = new();

        public int CallCount { get; private set; }
        public LegalAnalysisReviewInput? LastInput { get; private set; }

        public Task<LegalAnalysisReviewResult> ReviewAsync(
            LegalAnalysisReviewInput input,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastInput = input;
            return _inner.ReviewAsync(input, cancellationToken);
        }
    }

    private enum MalformedQueryResponse
    {
        NullResult,
        NullCitation
    }

    private sealed class MalformedQueryCnvRegulationMcpClient(
        MalformedQueryResponse malformedResponse,
        bool succeedAfterMalformed = false) : ICnvRegulationMcpClient
    {
        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;
        public int CallCount { get; private set; }

        public Task<CnvRegulationDocumentResponse> GetDocumentAsync(
            CnvRegulationDocumentRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationDocumentResponse(false, null, [], []));

        public Task<CnvRegulationArticleResponse> GetArticleAsync(
            CnvRegulationArticleRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationArticleResponse(false, null, null, 0, []));

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (succeedAfterMalformed && CallCount > 1)
            {
                return Task.FromResult(CreateSuccessfulResponse(request.Query));
            }

            IReadOnlyList<CnvRegulationSearchResult> results = malformedResponse switch
            {
                MalformedQueryResponse.NullResult =>
                [
                    CreateResult(
                        "shared-artifact",
                        "Shared artifact",
                        "Shared cited evidence.",
                        [CreateCitation()]),
                    null!
                ],
                MalformedQueryResponse.NullCitation =>
                [
                    CreateResult(
                        "shared-artifact",
                        "Shared artifact",
                        "Shared cited evidence.",
                        [CreateCitation(), null!])
                ],
                _ => throw new ArgumentOutOfRangeException(nameof(malformedResponse))
            };

            return Task.FromResult(new CnvRegulationSearchResponse(
                request.Query,
                results,
                ["discarded malformed response warning"]
            ));
        }

        private static CnvRegulationSearchResponse CreateSuccessfulResponse(
            string query)
        {
            return new CnvRegulationSearchResponse(
                query,
                [
                    CreateResult(
                        "shared-artifact",
                        "Shared artifact",
                        "Shared cited evidence.",
                        [CreateCitation()])
                ],
                []
            );
        }
    }

    private sealed class SensitiveThrowingCnvRegulationMcpClient(
        string secret) : ICnvRegulationMcpClient
    {
        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;

        public Task<CnvRegulationDocumentResponse> GetDocumentAsync(
            CnvRegulationDocumentRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationDocumentResponse(false, null, [], []));

        public Task<CnvRegulationArticleResponse> GetArticleAsync(
            CnvRegulationArticleRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationArticleResponse(false, null, null, 0, []));

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException($"{secret}: {request.Query}");
        }
    }

    private sealed record CapturedLog(
        LogLevel Level,
        string Message,
        string State,
        Exception? Exception);

    private sealed class CapturingLogger : ILogger<McpRegulatoryKnowledgeSource>
    {
        public List<CapturedLog> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new CapturedLog(
                logLevel,
                formatter(state, exception),
                state?.ToString() ?? string.Empty,
                exception
            ));
        }
    }

    private sealed class OrderedActivityEventPublisher(
        List<string> steps,
        bool cancelOnPlanEvent = false) : IActivityEventPublisher
    {
        public List<ActivityEvent> PublishedEvents { get; } = [];

        public Task PublishAsync(
            ActivityEvent @event,
            CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(@event);
            steps.Add($"event:{@event.Type}");

            var isPlanEvent =
                @event.Type == "legal_cnv_queries_derived" ||
                @event.Type == "legal_cnv_query_fallback_used";
            if (cancelOnPlanEvent && isPlanEvent)
            {
                throw new OperationCanceledException(
                    "Publisher canceled without token state.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeActivityEventPublisher : IActivityEventPublisher
    {
        public List<ActivityEvent> PublishedEvents { get; } = [];

        public Task PublishAsync(ActivityEvent @event, CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingCnvRegulationMcpClient : ICnvRegulationMcpClient
    {
        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;

        public Task<CnvRegulationDocumentResponse> GetDocumentAsync(
            CnvRegulationDocumentRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationDocumentResponse(false, null, [], []));

        public Task<CnvRegulationArticleResponse> GetArticleAsync(
            CnvRegulationArticleRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationArticleResponse(false, null, null, 0, []));

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("transport failed at C:\\sensitive\\cnv\\server.log");
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"execution\":null,\"riskSignals\":[]}")]
    [InlineData("{\"riskSignals\":null,\"execution\":{\"overallStatus\":1,\"stages\":[]}}")]
    [InlineData("{\"riskSignals\":[null],\"execution\":{\"overallStatus\":1,\"stages\":[]}}")]
    [InlineData("{\"riskSignals\":[],\"execution\":{\"overallStatus\":1,\"stages\":[null]}}")]
    [InlineData("{\"riskSignals\":[],\"execution\":{\"overallStatus\":1,\"stages\":null}}")]
    [InlineData("{ invalid")]
    public async Task Plugin_IncompleteFinancialAnalysis_UsesProvidedOnlyFallback(
        string invalidFinancialAnalysisJson)
    {
        var probe = await ReviewThroughPluginAsync(
            invalidFinancialAnalysisJson,
            dataEvidenceJson: null
        );

        probe.Strategy.ReceivedFinancialAnalysis.Should().BeNull();
        probe.Client.ReceivedRequests.Select(item => item.Query).Should().Equal(
            "régimen informativo estados financieros emisoras"
        );
        probe.Client.ReceivedRequests.Should()
            .NotContain(item => item.Query.Contains("liquidez"));
        probe.Result.QueryStrategy.Should().BeOfType<LegalQueryStrategyAudit>()
            .Which.Source.Should().Be("fallback");
        probe.Result.LegalReview.Should().NotBeNull();
        probe.Result.LegalReview!.FailureReason.Should().Be(
            "financial_analysis_missing");
        probe.Result.RequiresHumanReview.Should().BeTrue();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"failedStages\":null}")]
    [InlineData("{\"dataToolStatus\":\"executed\",\"failedStages\":[null]}")]
    [InlineData("{\"fallbackReason\":\"sensitive arbitrary reason\",\"dataToolStatus\":\"sensitive arbitrary status\",\"failedStages\":null}")]
    public async Task Plugin_IncompleteDataEvidence_IsRejectedWithoutLeakage(
        string invalidDataEvidenceJson)
    {
        var probe = await ReviewThroughPluginAsync(
            financialAnalysisJson: null,
            dataEvidenceJson: invalidDataEvidenceJson
        );
        var expectedEvidence = LegalDataEvidenceClassifier.Classify(
            LegalDataToolStatuses.Executed,
            financialAnalysis: null
        );

        probe.Strategy.ReceivedFinancialAnalysis.Should().BeNull();
        probe.Strategy.ReceivedDataEvidence.Should().BeEquivalentTo(
            expectedEvidence);
        probe.Client.ReceivedRequests.Select(item => item.Query).Should().Equal(
            "régimen informativo estados financieros emisoras"
        );
        System.Text.Json.JsonSerializer.Serialize(probe.Result)
            .Should().NotContain("sensitive arbitrary");
    }

    [Fact]
    public async Task ReviewAsync_PartialFailure_PreservesSuccessfulEvidenceAndAuditsEveryQuery()
    {
        await using var dbContext =
            StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var plan = CreateAuditPlan();
        var client = new PartialFailureCnvRegulationMcpClient();
        var reviewService = new TrackingLegalAnalysisReviewService();
        var source = CreateSource(
            client,
            dbContext,
            new StaticLegalCnvQueryStrategy(plan),
            reviewService: reviewService
        );

        var result = await source.ReviewAsync(
            new RegulatoryReviewRequest(
                CreateReportWithFinancialAnalysis(),
                new LegalReviewContext(
                    FinancialAnalysisResolutionMode.ProvidedOnly)),
            CancellationToken.None
        );

        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.Findings.Should().ContainSingle();
        result.LegalReview.Should().NotBeNull();
        result.LegalReview!.EvidenceReferences.Should().NotBeEmpty();
        result.RequiresHumanReview.Should().BeTrue();
        result.Warnings.Should().Contain(
            "La búsqueda en la CNV a través de MCP falló para una consulta.");
        result.Warnings.Should().NotContain(item =>
            item.Contains("sensitive partial failure payload"));
        reviewService.CallCount.Should().Be(1);

        var audit = result.QueryStrategy.Should()
            .BeOfType<LegalQueryStrategyAudit>().Subject;
        audit.StrategyVersion.Should().Be("audit_strategy_v1");
        audit.Source.Should().Be(LegalCnvQuerySources.Contextual);
        audit.FallbackReason.Should().BeNull();
        audit.DataToolStatus.Should().Be(LegalDataToolStatuses.Executed);
        audit.FinancialAnalysisStatus.Should().Be(
            FinancialAnalysisExecutionStatus.Succeeded);
        audit.FailedStages.Should().BeEmpty();
        audit.Queries.Should().HaveCount(2);
        audit.Queries[0].Index.Should().Be(1);
        audit.Queries[0].Total.Should().Be(2);
        audit.Queries[0].ExecutionStatus.Should().Be("succeeded");
        audit.Queries[0].ResultCount.Should().Be(1);
        audit.Queries[0].CitedEvidenceCount.Should().Be(1);
        audit.Queries[1].Index.Should().Be(2);
        audit.Queries[1].Total.Should().Be(2);
        audit.Queries[1].ExecutionStatus.Should().Be("failed");
        audit.Queries[1].ResultCount.Should().Be(0);
        audit.Queries[1].CitedEvidenceCount.Should().Be(0);
    }

    [Fact]
    public async Task ReviewAsync_AllQueriesFail_ReturnsUnknownAndSkipsAiReview()
    {
        await using var dbContext =
            StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var reviewService = new TrackingLegalAnalysisReviewService();
        var source = CreateSource(
            new AlwaysFailingCnvRegulationMcpClient(),
            dbContext,
            new StaticLegalCnvQueryStrategy(CreateAuditPlan()),
            reviewService: reviewService
        );

        var result = await source.ReviewAsync(
            CreateReportWithFinancialAnalysis(),
            CancellationToken.None
        );

        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.Findings.Should().BeEmpty();
        result.RequiresHumanReview.Should().BeTrue();
        result.LegalReview.Should().NotBeNull();
        result.LegalReview!.FailureReason.Should().Be("cnv_queries_failed");
        result.LegalReview.EvidenceReferences.Should().BeEmpty();
        reviewService.CallCount.Should().Be(0);

        var audit = result.QueryStrategy.Should()
            .BeOfType<LegalQueryStrategyAudit>().Subject;
        audit.Queries.Should().HaveCount(2);
        audit.Queries.Should().OnlyContain(item =>
            item.ExecutionStatus == "failed" &&
            item.ResultCount == 0 &&
            item.CitedEvidenceCount == 0);
    }

    [Fact]
    public async Task ReviewAsync_ValidResultThenNullResult_DiscardsEntireFailedQuery()
    {
        await AssertMalformedSingleQueryIsAtomicAsync(
            MalformedQueryResponse.NullResult);
    }

    [Fact]
    public async Task ReviewAsync_ValidCitationThenNullCitation_DiscardsEntireFailedQuery()
    {
        await AssertMalformedSingleQueryIsAtomicAsync(
            MalformedQueryResponse.NullCitation);
    }

    [Fact]
    public async Task ReviewAsync_FailedQueryArtifacts_DoNotPoisonLaterSuccessfulDeduplication()
    {
        await using var dbContext =
            StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var reviewService = new TrackingLegalAnalysisReviewService();
        var source = CreateSource(
            new MalformedQueryCnvRegulationMcpClient(
                MalformedQueryResponse.NullResult,
                succeedAfterMalformed: true),
            dbContext,
            new StaticLegalCnvQueryStrategy(CreateAuditPlan()),
            reviewService: reviewService
        );

        var result = await source.ReviewAsync(
            CreateReportWithFinancialAnalysis(),
            CancellationToken.None
        );

        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.RequiresHumanReview.Should().BeTrue();
        result.Findings.Should().ContainSingle();
        result.Warnings.Should().NotContain(
            "discarded malformed response warning");
        reviewService.CallCount.Should().Be(1);
        reviewService.LastInput.Should().NotBeNull();
        reviewService.LastInput!.CnvEvidence.Should().ContainSingle()
            .Which.RegulationArea.Should().Be("audit_area_2");

        var audit = result.QueryStrategy.Should()
            .BeOfType<LegalQueryStrategyAudit>().Subject;
        audit.Queries.Select(item => item.ExecutionStatus).Should().Equal(
            "failed",
            "succeeded");
        audit.Queries[0].ResultCount.Should().Be(0);
        audit.Queries[0].CitedEvidenceCount.Should().Be(0);
        audit.Queries[1].ResultCount.Should().Be(1);
        audit.Queries[1].CitedEvidenceCount.Should().Be(1);
    }

    [Fact]
    public async Task ReviewAsync_QueryFailureLog_DoesNotRetainExceptionQueryOrSecret()
    {
        const string secret = "super-secret-mcp-payload";
        await using var dbContext =
            StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var logger = new CapturingLogger();
        var source = CreateSource(
            new SensitiveThrowingCnvRegulationMcpClient(secret),
            dbContext,
            new StaticLegalCnvQueryStrategy(CreateAuditPlan(queryCount: 1)),
            logger: logger
        );

        var result = await source.ReviewAsync(
            CreateReportWithFinancialAnalysis(),
            CancellationToken.None
        );

        var log = logger.Entries.Should().ContainSingle(item =>
            item.Message == "CNV MCP search failed for query 1 of 1.")
            .Subject;
        log.Exception.Should().BeNull();
        log.Message.Should().NotContain(secret).And.NotContain("audit query 1");
        log.State.Should().NotContain(secret).And.NotContain("audit query 1");
        result.Warnings.Should().Contain(
            "La búsqueda en la CNV a través de MCP falló para una consulta.");
    }

    private static async Task AssertMalformedSingleQueryIsAtomicAsync(
        MalformedQueryResponse malformedResponse)
    {
        await using var dbContext =
            StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var reviewService = new TrackingLegalAnalysisReviewService();
        var source = CreateSource(
            new MalformedQueryCnvRegulationMcpClient(malformedResponse),
            dbContext,
            new StaticLegalCnvQueryStrategy(CreateAuditPlan(queryCount: 1)),
            reviewService: reviewService
        );

        var result = await source.ReviewAsync(
            CreateReportWithFinancialAnalysis(),
            CancellationToken.None
        );

        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.RequiresHumanReview.Should().BeTrue();
        result.Findings.Should().BeEmpty();
        result.Warnings.Should().NotContain(
            "discarded malformed response warning");
        result.LegalReview.Should().NotBeNull();
        result.LegalReview!.FailureReason.Should().Be("cnv_queries_failed");
        result.LegalReview.EvidenceReferences.Should().BeEmpty();
        reviewService.CallCount.Should().Be(0);

        var audit = result.QueryStrategy.Should()
            .BeOfType<LegalQueryStrategyAudit>().Subject;
        var queryAudit = audit.Queries.Should().ContainSingle().Subject;
        queryAudit.ExecutionStatus.Should().Be("failed");
        queryAudit.ResultCount.Should().Be(0);
        queryAudit.CitedEvidenceCount.Should().Be(0);
    }

    [Fact]
    public async Task ReviewAsync_UncitedSuccessfulResult_IsLowRiskSuccessfulAudit()
    {
        await using var dbContext =
            StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var reviewService = new TrackingLegalAnalysisReviewService();
        var source = CreateSource(
            new UncitedCnvRegulationMcpClient(),
            dbContext,
            new StaticLegalCnvQueryStrategy(CreateAuditPlan(queryCount: 1)),
            reviewService: reviewService
        );

        var result = await source.ReviewAsync(
            CreateReportWithFinancialAnalysis(),
            CancellationToken.None
        );

        result.RiskLevel.Should().Be("NotEstablished");
        result.Findings.Should().BeEmpty();
        result.RequiresHumanReview.Should().BeTrue();
        reviewService.CallCount.Should().Be(1);
        var queryAudit = result.QueryStrategy.Should()
            .BeOfType<LegalQueryStrategyAudit>().Subject
            .Queries.Should().ContainSingle().Subject;
        queryAudit.ExecutionStatus.Should().Be("succeeded");
        queryAudit.ResultCount.Should().Be(1);
        queryAudit.CitedEvidenceCount.Should().Be(0);
    }

    [Fact]
    public async Task ReviewAsync_McpCancellationWithoutCanceledToken_PropagatesBeforeEventsOrAi()
    {
        await using var dbContext =
            StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var steps = new List<string>();
        var publisher = new OrderedActivityEventPublisher(steps);
        var reviewService = new TrackingLegalAnalysisReviewService();
        var source = CreateSource(
            new CancelingCnvRegulationMcpClient(),
            dbContext,
            new StaticLegalCnvQueryStrategy(CreateAuditPlan()),
            publisher,
            reviewService
        );

        Func<Task> act = () => source.ReviewAsync(
            CreateReportWithFinancialAnalysis(),
            CancellationToken.None
        );

        await act.Should().ThrowAsync<OperationCanceledException>();
        publisher.PublishedEvents.Should().BeEmpty();
        reviewService.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ReviewAsync_PlanEventCancellation_PropagatesBeforeAiCompletion()
    {
        await using var dbContext =
            StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var steps = new List<string>();
        var client = new OrderedSuccessCnvRegulationMcpClient(steps);
        var publisher = new OrderedActivityEventPublisher(
            steps,
            cancelOnPlanEvent: true
        );
        var reviewService = new TrackingLegalAnalysisReviewService();
        var source = CreateSource(
            client,
            dbContext,
            new StaticLegalCnvQueryStrategy(CreateAuditPlan()),
            publisher,
            reviewService
        );

        Func<Task> act = () => source.ReviewAsync(
            CreateReportWithFinancialAnalysis(),
            CancellationToken.None
        );

        await act.Should().ThrowAsync<OperationCanceledException>();
        steps.Should().Equal(
            "query:1",
            "query:2",
            "event:legal_cnv_queries_derived"
        );
        reviewService.CallCount.Should().Be(0);
        publisher.PublishedEvents.Should().NotContain(item =>
            item.Type == "legal_agent_ai_review_completed");
    }

    [Theory]
    [InlineData(false, "legal_cnv_queries_derived")]
    [InlineData(true, "legal_cnv_query_fallback_used")]
    public async Task ReviewAsync_PublishesPlanEventAfterQueriesWithFullImmutableAudit(
        bool fallback,
        string expectedEventType)
    {
        await using var dbContext =
            StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var steps = new List<string>();
        var client = new OrderedSuccessCnvRegulationMcpClient(steps);
        var publisher = new OrderedActivityEventPublisher(steps);
        var plan = CreateAuditPlan(fallback);
        var source = CreateSource(
            client,
            dbContext,
            new StaticLegalCnvQueryStrategy(plan),
            publisher
        );

        var result = await source.ReviewAsync(
            CreateReportWithFinancialAnalysis(),
            CancellationToken.None
        );

        steps.Take(3).Should().Equal(
            "query:1",
            "query:2",
            $"event:{expectedEventType}"
        );
        publisher.PublishedEvents.Count(item =>
            item.Type == expectedEventType).Should().Be(1);
        publisher.PublishedEvents.Count(item =>
            item.Type == "legal_cnv_queries_derived" ||
            item.Type == "legal_cnv_query_fallback_used").Should().Be(1);
        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.RequiresHumanReview.Should().BeTrue();

        var audit = result.QueryStrategy.Should()
            .BeOfType<LegalQueryStrategyAudit>().Subject;
        audit.StrategyVersion.Should().Be(plan.StrategyVersion);
        audit.Source.Should().Be(plan.Source);
        audit.FallbackReason.Should().Be(plan.FallbackReason);
        audit.DataToolStatus.Should().Be(plan.DataEvidence.DataToolStatus);
        audit.FinancialAnalysisStatus.Should().Be(
            plan.DataEvidence.FinancialAnalysisStatus);
        audit.FailedStages.Should().NotBeSameAs(
            plan.DataEvidence.FailedStages);
        ReferenceEquals(audit.Queries, plan.Queries).Should().BeFalse();
        audit.Queries.Select(item => item.Index).Should().Equal(1, 2);
        audit.Queries.Should().OnlyContain(item =>
            item.Total == 2 &&
            item.ExecutionStatus == "succeeded" &&
            item.ResultCount == 1 &&
            item.CitedEvidenceCount == 1);

        var failedStages = audit.FailedStages.Should()
            .BeAssignableTo<System.Collections.IList>().Subject;
        var queryAudits = audit.Queries.Should()
            .BeAssignableTo<System.Collections.IList>().Subject;
        var relatedSignals = audit.Queries[0].RelatedFinancialSignals.Should()
            .BeAssignableTo<IList<string>>().Subject;
        failedStages.IsReadOnly.Should().BeTrue();
        queryAudits.IsReadOnly.Should().BeTrue();
        relatedSignals.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public async Task ReviewAsync_ProvidedOnly_UsesSuppliedFailureEvidenceWithoutLoadingPersistedAnalysis()
    {
        await using var dbContext =
            StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var client = new QueryTrackingCnvRegulationMcpClient();
        var publisher = new FakeActivityEventPublisher();
        var source = CreateSource(
            client,
            dbContext,
            new FinancialAnalysisLegalCnvQueryStrategy(),
            publisher
        );
        var persistedAnalysis = CreateFinancialAnalysis(
            new FinancialRiskSignal(
                "liquidity_risk",
                "High",
                "Q1",
                "Low current ratio",
                Array.Empty<RiskEvidenceItem>())
        );
        var session = await PersistFinancialAnalysisAsync(
            dbContext,
            persistedAnalysis
        );
        var report = CreateReport() with { SessionId = session.Id };
        var dataEvidence = LegalDataEvidenceClassifier.Classify(
            LegalDataToolStatuses.Failed,
            financialAnalysis: null
        );
        var request = new RegulatoryReviewRequest(
            report,
            new LegalReviewContext(
                FinancialAnalysisResolutionMode.ProvidedOnly,
                dataEvidence)
        );

        var result = await source.ReviewAsync(request, CancellationToken.None);

        client.ReceivedRequests.Select(item => item.Query).Should().Equal(
            "régimen informativo estados financieros emisoras"
        );
        result.QueryStrategy.Should().BeOfType<LegalQueryStrategyAudit>()
            .Which.Source.Should().Be("fallback");
        result.LegalReview.Should().NotBeNull();
        result.LegalReview!.FailureReason.Should().Be("financial_analysis_missing");
        publisher.PublishedEvents.Should()
            .NotContain(item => item.Type == "legal_cnv_queries_derived");
    }

    [Fact]
    public async Task ReviewAsync_LegacyProvidedAnalysis_UsesAuditableFallbackWithoutDerivedEvent()
    {
        await using var dbContext =
            StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var client = new QueryTrackingCnvRegulationMcpClient();
        var publisher = new FakeActivityEventPublisher();
        var source = CreateSource(
            client,
            dbContext,
            new FinancialAnalysisLegalCnvQueryStrategy(),
            publisher
        );
        var legacyAnalysis = CreateFinancialAnalysis(
            new FinancialRiskSignal(
                "liquidity_risk",
                "High",
                "Q1",
                "Low current ratio",
                Array.Empty<RiskEvidenceItem>())
        ) with
        {
            Execution = FinancialAnalysisExecution.LegacyUnknown
        };
        var report = CreateReport() with
        {
            FinancialAnalysis = legacyAnalysis
        };
        var request = new RegulatoryReviewRequest(
            report,
            new LegalReviewContext(
                FinancialAnalysisResolutionMode.ProvidedOnly)
        );

        var result = await source.ReviewAsync(request, CancellationToken.None);

        client.ReceivedRequests.Select(item => item.Query).Should().Equal(
            "régimen informativo estados financieros emisoras"
        );
        var audit = result.QueryStrategy.Should()
            .BeOfType<LegalQueryStrategyAudit>().Subject;
        audit.Source.Should().Be("fallback");
        audit.Queries.Should().ContainSingle()
            .Which.Reason.Should().Contain("No había señales específicas");
        publisher.PublishedEvents.Should()
            .NotContain(item => item.Type == "legal_cnv_queries_derived");
    }

    [Fact]
    public async Task ReviewAsync_Should_use_queries_from_ILegalCnvQueryStrategy_and_pass_risk_signals()
    {
        // Arrange
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var client = new QueryTrackingCnvRegulationMcpClient();
        var publisher = new FakeActivityEventPublisher();
        var source = CreateSource(client, dbContext, new FinancialAnalysisLegalCnvQueryStrategy(), publisher);

        var signals = new[]
        {
            new FinancialRiskSignal("liquidity_risk", "High", "Q1", "Low current ratio", Array.Empty<RiskEvidenceItem>())
        };

        var financialAnalysis = CreateFinancialAnalysis(signals);
        var session = await PersistFinancialAnalysisAsync(
            dbContext,
            financialAnalysis
        );

        var report = new FinancialReportContext(
            SessionId: session.Id,
            ReportName: "test-report",
            TotalAmount: 1000m,
            TransactionCount: 1,
            SubmittedAt: DateTimeOffset.UtcNow
        );

        // Act
        var result = await source.ReviewAsync(
            new RegulatoryReviewRequest(report, LegalReviewContext.Default),
            CancellationToken.None
        );

        // Assert
        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.Findings.Should().NotBeEmpty();
        result.Findings.Any(f => f.Finding.Contains("liquidez")).Should().BeTrue();

        client.ReceivedRequests.Should().NotBeEmpty();
        client.ReceivedRequests.Any(r => r.Query.Contains("liquidez")).Should().BeTrue();

        publisher.PublishedEvents.Should().Contain(e => e.Type == "legal_cnv_queries_derived");
        publisher.PublishedEvents.Should().Contain(e => e.Type == "legal_agent_ai_review_completed");
    }

    [Fact]
    public async Task ReviewAsync_Should_not_publish_derived_query_event_when_using_fallback_queries()
    {
        // Arrange
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var client = new QueryTrackingCnvRegulationMcpClient();
        var publisher = new FakeActivityEventPublisher();
        var source = CreateSource(client, dbContext, new FinancialAnalysisLegalCnvQueryStrategy(), publisher);

        var report = new FinancialReportContext(
            SessionId: Guid.NewGuid(),
            ReportName: "test-report",
            TotalAmount: 1000m,
            TransactionCount: 1,
            SubmittedAt: DateTimeOffset.UtcNow
        );

        // Act
        var result = await source.ReviewAsync(report, CancellationToken.None);

        // Assert
        result.QueryStrategy.Should().BeOfType<LegalQueryStrategyAudit>().Which.Source.Should().Be("fallback");
        publisher.PublishedEvents.Should().NotContain(e => e.Type == "legal_cnv_queries_derived");
        publisher.PublishedEvents.Should().Contain(e => e.Type == "legal_agent_ai_review_completed");
    }

    [Fact]
    public async Task ReviewAsync_Should_use_safe_warning_when_mcp_search_throws()
    {
        // Arrange
        var source = CreateSource(new ThrowingCnvRegulationMcpClient());

        // Act
        var result = await source.ReviewAsync(
            CreateReport(),
            CancellationToken.None
        );

        // Assert
        result.Warnings.Should().Contain("La búsqueda en la CNV a través de MCP falló para una consulta.");
        result.Warnings.Should().NotContain(w => w.Contains("transport failed"));
        result.Warnings.Should().NotContain(w => w.Contains("C:\\sensitive"));
    }

    [Fact]
    public async Task ReviewAsync_Should_combine_and_deduplicate_results_from_multiple_CNV_queries()
    {
        // Arrange
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var client = new QueryTrackingCnvRegulationMcpClient();
        var source = CreateSource(client, dbContext, new FinancialAnalysisLegalCnvQueryStrategy());

        var signals = new[]
        {
            new FinancialRiskSignal("liquidity_risk", "High", "Q1", "Low current ratio", Array.Empty<RiskEvidenceItem>()),
            new FinancialRiskSignal("leverage_warning", "High", "Q1", "High debt", Array.Empty<RiskEvidenceItem>())
        };

        var financialAnalysis = CreateFinancialAnalysis(signals);
        var session = await PersistFinancialAnalysisAsync(
            dbContext,
            financialAnalysis
        );

        var report = new FinancialReportContext(
            SessionId: session.Id,
            ReportName: "test-report",
            TotalAmount: 1000m,
            TransactionCount: 1,
            SubmittedAt: DateTimeOffset.UtcNow
        );

        // Act
        var result = await source.ReviewAsync(report, CancellationToken.None);

        // Assert
        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        // Should contain findings from both queries (liquidez and leverage)
        result.Findings.Should().Contain(f => f.Finding.Contains("liquidez"));
        result.Findings.Should().Contain(f => f.Finding.Contains("leverage"));
    }

    [Fact]
    public async Task ReviewAsync_Should_warn_and_ignore_uncited_evidence_as_strong_support()
    {
        // Arrange
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var client = new QueryTrackingCnvRegulationMcpClient();
        var source = CreateSource(client, dbContext, new FinancialAnalysisLegalCnvQueryStrategy());

        var signals = new[]
        {
            new FinancialRiskSignal("margin_deterioration", "High", "Q1", "Low profit", Array.Empty<RiskEvidenceItem>())
        };

        var financialAnalysis = CreateFinancialAnalysis(signals);
        var session = await PersistFinancialAnalysisAsync(
            dbContext,
            financialAnalysis
        );

        var report = new FinancialReportContext(
            SessionId: session.Id,
            ReportName: "test-report",
            TotalAmount: 1000m,
            TransactionCount: 1,
            SubmittedAt: DateTimeOffset.UtcNow
        );

        // Act
        var result = await source.ReviewAsync(report, CancellationToken.None);

        // Assert
        // Margins map to "resultados", which returns uncited result
        result.Findings.Should().NotContain(f => f.Finding.Contains("uncited"));
        result.Warnings.Should().Contain(w => w.Contains("Algunos resultados de búsqueda de CNV/Infoleg se ignoraron como evidencia sólida debido a que no incluían citas."));
        result.LegalReview.Should().NotBeNull();
        result.LegalReview!.PossibleRegulatoryReviewAreas.Should().BeEmpty();
        result.LegalReview.EvidenceReferences.Should().BeEmpty();
        result.LegalReview.Warnings.Should().Contain("Había señales de riesgo financiero, pero no había evidencia CNV/Infoleg citada disponible.");
    }

    [Fact]
    public async Task ReviewAsync_Should_still_work_with_fallback_when_financialAnalysis_missing()
    {
        // Arrange
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var client = new QueryTrackingCnvRegulationMcpClient();
        var source = CreateSource(client, dbContext, new FinancialAnalysisLegalCnvQueryStrategy());

        var report = new FinancialReportContext(
            SessionId: Guid.NewGuid(), // Not in DB
            ReportName: "test-report",
            TotalAmount: 1000m,
            TransactionCount: 1,
            SubmittedAt: DateTimeOffset.UtcNow
        );

        // Act
        var result = await source.ReviewAsync(report, CancellationToken.None);

        // Assert
        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.Findings.Should().Contain(f => f.Finding.Contains("fallback"));
        result.Warnings.Should().Contain(w => w.Contains("No había señales de riesgo financiero específicas disponibles; utilizando una consulta general de información financiera."));
        result.LegalReview.Should().NotBeNull();
        result.LegalReview!.UsedLlm.Should().BeFalse();
        result.LegalReview.UsedFallback.Should().BeTrue();
        result.LegalReview.FailureReason.Should().Be("financial_analysis_missing");
    }
}
