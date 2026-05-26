using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Shared;
using Orchestration.Application.Agents.Legal.Cnv;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Application.Persistence;
using Orchestration.Tests.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Activity;
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
            "Automated regulatory retrieval only. Human legal review is required before making operational decisions."
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
            "No cited CNV regulatory evidence was found by the MCP search strategy."
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
            "Automated regulatory retrieval only. Human legal review is required before making operational decisions."
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

        var financialAnalysis = new FinancialAnalysisContext(
            Engine: "TestDataEngine",
            DocumentId: "doc-123",
            Company: "TestCorp",
            Ratios: Array.Empty<FinancialRatio>(),
            Comparisons: Array.Empty<FinancialPeriodComparison>(),
            RiskSignals: signals,
            RiskEvidence: Array.Empty<RiskEvidenceItem>(),
            Warnings: Array.Empty<string>(),
            Limitations: Array.Empty<string>()
        );

        var session = AnalysisSession.Create();
        session.SetContext(System.Text.Json.JsonSerializer.Serialize(new { financialAnalysis }));
        
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();

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
        result.Warnings.Should().Contain("CNV MCP search failed for one query. See application logs for details.");
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

        var financialAnalysis = new FinancialAnalysisContext(
            Engine: "TestDataEngine",
            DocumentId: "doc-123",
            Company: "TestCorp",
            Ratios: Array.Empty<FinancialRatio>(),
            Comparisons: Array.Empty<FinancialPeriodComparison>(),
            RiskSignals: signals,
            RiskEvidence: Array.Empty<RiskEvidenceItem>(),
            Warnings: Array.Empty<string>(),
            Limitations: Array.Empty<string>()
        );

        var session = AnalysisSession.Create();
        session.SetContext(System.Text.Json.JsonSerializer.Serialize(new { financialAnalysis }));
        
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();

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

        var financialAnalysis = new FinancialAnalysisContext(
            Engine: "TestDataEngine",
            DocumentId: "doc-123",
            Company: "TestCorp",
            Ratios: Array.Empty<FinancialRatio>(),
            Comparisons: Array.Empty<FinancialPeriodComparison>(),
            RiskSignals: signals,
            RiskEvidence: Array.Empty<RiskEvidenceItem>(),
            Warnings: Array.Empty<string>(),
            Limitations: Array.Empty<string>()
        );

        var session = AnalysisSession.Create();
        session.SetContext(System.Text.Json.JsonSerializer.Serialize(new { financialAnalysis }));
        
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();

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
        result.Warnings.Should().Contain(w => w.Contains("Some CNV/Infoleg search results were ignored as strong evidence because they did not include citations."));
        result.LegalReview.Should().NotBeNull();
        result.LegalReview!.PossibleRegulatoryReviewAreas.Should().BeEmpty();
        result.LegalReview.EvidenceReferences.Should().BeEmpty();
        result.LegalReview.Warnings.Should().Contain("Financial risk signals were present, but no cited CNV/Infoleg evidence was available.");
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
        result.Warnings.Should().Contain(w => w.Contains("No specific financial risk signals were available; using a general financial reporting query."));
        result.LegalReview.Should().NotBeNull();
        result.LegalReview!.UsedLlm.Should().BeFalse();
        result.LegalReview.UsedFallback.Should().BeTrue();
        result.LegalReview.FailureReason.Should().Be("financial_analysis_missing");
    }
}
