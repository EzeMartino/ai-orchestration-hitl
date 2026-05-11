using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

namespace Orchestration.Tests.Agents.Legal;

public sealed class FakeCnvRegulationMcpClient : ICnvRegulationMcpClient
{
    public Task<CnvRegulationSearchResponse> SearchAsync(
        CnvRegulationSearchRequest request,
        CancellationToken cancellationToken)
    {
        var response = new CnvRegulationSearchResponse(
            request.Query,
            [
                new CnvRegulationSearchResult(
                    DocumentId: "infoleg-rg-622-2013-texact",
                    ChunkId: "infoleg-rg-622-2013-texact-articulo-1",
                    Title: "Resolución General 622/2013 - Texto actualizado",
                    Chapter: "Capitulo I",
                    Section: null,
                    Article: "Articulo 1",
                    Source: "Infoleg",
                    Url: "https://servicios.infoleg.gob.ar/",
                    Snippet: "Texto encontrado...",
                    Score: 0.123,
                    Citations:
                    [
                        new CnvRegulationCitation(
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
                        )
                    ]
                )
            ],
            Warnings: []
        );

        return Task.FromResult(response);
    }
}