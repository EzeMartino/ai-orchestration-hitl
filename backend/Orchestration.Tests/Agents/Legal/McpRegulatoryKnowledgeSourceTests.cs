using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
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
    public async Task ReviewAsync_Should_map_cited_mcp_results_to_regulatory_findings()
    {
        var source = CreateSource(new FakeCnvRegulationMcpClient());

        var result = await source.ReviewAsync(
            CreateReport(),
            CancellationToken.None
        );

        result.SourceEngine.Should().Be("MCP CNV Regulation Server");
        result.HasComplianceRisk.Should().BeTrue();
        result.RiskLevel.Should().Be("Medium");
        result.Findings.Should().NotBeEmpty();
        result.Warnings.Should().Contain(
            "Recuperación regulatoria automatizada únicamente. Se requiere una revisión legal humana antes de tomar decisiones operativas."
        );

        result.Findings
            .Should()
            .Contain(x =>
                x.Regulation.Contains("622/2013") &&
                x.Section == "Articulo 1" &&
                x.Finding == "Texto normativo citado."
            );
    }

    [Fact]
    public async Task ReviewAsync_Should_map_only_cited_mcp_results_to_regulatory_findings()
    {
        var source = CreateSource(new MixedCitationCnvRegulationMcpClient());

        var result = await source.ReviewAsync(
            CreateReport(),
            CancellationToken.None
        );

        result.HasComplianceRisk.Should().BeTrue();
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
        result.RiskLevel.Should().Be("Low");
        result.Findings.Should().BeEmpty();
        result.Warnings.Should().Contain("candidate source");
        result.Warnings.Should().Contain(
            "No se encontró evidencia regulatoria citada de la CNV mediante la estrategia de búsqueda MCP."
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
            "Recuperación regulatoria automatizada únicamente. Se requiere una revisión legal humana antes de tomar decisiones operativas."
        );
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
                DefaultLimit = 5
            }
        );

        return new McpRegulatoryKnowledgeSource(
            client,
            options,
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

    private sealed class MixedCitationCnvRegulationMcpClient : ICnvRegulationMcpClient
    {
        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;

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
        IReadOnlyList<CnvRegulationCitation> citations)
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
            Score: 0.123,
            Citations: citations
        );
    }

    private static CnvRegulationCitation CreateCitation()
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
            Url: "https://servicios.infoleg.gob.ar/",
            QuotedText: "Texto normativo citado."
        );
    }

    // ==========================================
    // Phase 10 Block 10.4 Integration Tests
    // ==========================================

    private static McpRegulatoryKnowledgeSource CreateSource(
        ICnvRegulationMcpClient client,
        IOrchestrationDbContext dbContext,
        ILegalCnvQueryStrategy? queryStrategy = null,
        IActivityEventPublisher? activityPublisher = null,
        ILegalAnalysisReviewService? reviewService = null)
    {
        var options = Options.Create(
            new CnvRegulationMcpOptions
            {
                Enabled = true,
                Command = "dotnet",
                Args = [],
                DefaultLimit = 5
            }
        );

        return new McpRegulatoryKnowledgeSource(
            client,
            options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<McpRegulatoryKnowledgeSource>.Instance,
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
        result.HasComplianceRisk.Should().BeTrue();
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
        result.Warnings.Should().Contain("La búsqueda en la CNV a través de MCP falló para una consulta. Consulte los registros de la aplicación para más detalles.");
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
        result.HasComplianceRisk.Should().BeTrue();
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
        result.HasComplianceRisk.Should().BeTrue();
        result.Findings.Should().Contain(f => f.Finding.Contains("fallback"));
        result.Warnings.Should().Contain(w => w.Contains("No había señales de riesgo financiero específicas disponibles; utilizando una consulta general de información financiera."));
        result.LegalReview.Should().NotBeNull();
        result.LegalReview!.UsedLlm.Should().BeFalse();
        result.LegalReview.UsedFallback.Should().BeTrue();
        result.LegalReview.FailureReason.Should().Be("financial_analysis_missing");
    }
}
