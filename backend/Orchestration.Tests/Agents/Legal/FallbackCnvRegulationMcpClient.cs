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
                DefaultLimit = 5
            }
        );

        var source = new McpRegulatoryKnowledgeSource(
            client,
            options,
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

        result.HasComplianceRisk.Should().BeTrue();
        result.Findings.Should().NotBeEmpty();
        result.Warnings.Should().Contain(
            "Recuperación regulatoria automatizada únicamente. Se requiere una revisión legal humana antes de tomar decisiones operativas."
        );

        client.ReceivedQueries.Should().Contain("régimen informativo estados financieros emisoras");

        result.Findings
            .Should()
            .Contain(x => x.Finding == "Texto normativo citado desde fallback.");
    }
}

