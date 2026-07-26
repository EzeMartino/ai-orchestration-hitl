using Orchestration.Application.Agents.Legal.AiReview;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Shared;
using Orchestration.Infrastructure.Agents.Legal.Regulations;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

namespace Orchestration.Tests.Agents.Legal;

public sealed class FallbackCnvRegulationMcpClient : ICnvRegulationMcpClient
{
    public bool IsConnected => true;
    public int ColdStartCount => 0;
    public int ResetCount => 0;
    public string? LastError => null;

    public List<string> ReceivedQueries { get; } = [];

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
        ReceivedQueries.Add(request.Query);

        if (request.Query != "régimen informativo estados financieros emisoras")
        {
            return Task.FromResult(
                new CnvRegulationSearchResponse(
                    request.Query,
                    [],
                    []
                )
            );
        }

        return Task.FromResult(
            new CnvRegulationSearchResponse(
                request.Query,
                [
                    new CnvRegulationSearchResult(
                        DocumentId: "infoleg-rg-622-2013-norma",
                        ChunkId: "infoleg-rg-622-2013-norma-articulo-8",
                        Title: "Resolución General 622/2013 - Texto completo",
                        Chapter: "Capítulo I",
                        Section: null,
                        Article: "Artículo 8",
                        Source: "Infoleg",
                        Url: "https://servicios.infoleg.gob.ar/infolegInternet/anexos/215000-219999/219405/norma.htm",
                        Snippet: "Texto encontrado...",
                        Score: 0.075,
                        Citations:
                        [
                            new CnvRegulationCitation(
                                Source: "Infoleg",
                                DocumentType: "Resolución General",
                                ResolutionNumber: "622/2013",
                                Title: "Resolución General 622/2013 - Texto completo",
                                Chapter: "Capítulo I",
                                Section: null,
                                Article: "Artículo 8",
                                PublicationDate: "2013-09-09",
                                Url: "https://servicios.infoleg.gob.ar/infolegInternet/anexos/215000-219999/219405/norma.htm",
                                QuotedText: "Texto normativo citado desde fallback."
                            )
                        ]
                    )
                ],
                []
            )
        );
    }

    [Fact]
    public async Task ReviewAsync_Should_use_fallback_queries_until_cited_results_are_found()
    {
        var client = new FallbackCnvRegulationMcpClient();

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

        var source = new McpRegulatoryKnowledgeSource(
            client,
            options,
            enricher,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<McpRegulatoryKnowledgeSource>.Instance,
            new DeterministicLegalAnalysisReviewService()
        );

        var report = new FinancialReportContext(
            SessionId: Guid.NewGuid(),
            ReportName: "cnv-fallback-test-report",
            TotalAmount: 125000m,
            TransactionCount: 42,
            SubmittedAt: DateTimeOffset.UtcNow
        );

        var result = await source.ReviewAsync(
            report,
            CancellationToken.None
        );

        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.Findings.Should().NotBeEmpty();
        result.EvidenceAssessment.Should().NotBeNull();
        result.EvidenceAssessment!.EvidenceFound.Should().BeTrue();
        result.EvidenceAssessment.Relevance.Should().Be("None");
        result.EvidenceAssessment.EvidenceQuality.Should().Be("Strong");
        result.EvidenceAssessment.Severity.Should().Be("Info");
        result.EvidenceAssessment.RequiresHumanReview.Should().BeFalse();
        result.RequiresHumanReview.Should().BeTrue(
            "the fallback query strategy still requires human review");
        result.Summary.Should().Be(
            "Se recuperó evidencia regulatoria, pero no se estableció relevancia ni aplicabilidad para una evaluación de cumplimiento.");
        result.Warnings.Should().Contain(
            "Se recuperó evidencia regulatoria, pero no alcanzó el umbral de relevancia para crear un área de revisión."
        );
        result.LegalReview.Should().NotBeNull();
        result.LegalReview!.PossibleRegulatoryReviewAreas.Should().BeEmpty();
        result.LegalReview.EvidenceReferences.Should().BeEmpty();

        client.ReceivedQueries.Should().Contain("régimen informativo estados financieros emisoras");

        result.Findings
            .Should()
            .Contain(x => x.Finding == "Texto normativo citado desde fallback.");
    }
}

