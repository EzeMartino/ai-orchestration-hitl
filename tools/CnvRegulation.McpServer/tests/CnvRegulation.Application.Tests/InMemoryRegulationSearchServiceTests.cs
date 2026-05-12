using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.InMemory;
using CnvRegulation.Infrastructure.Repositories;
using CnvRegulation.Infrastructure.Search;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class InMemoryRegulationSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_ShouldReturnMockResults_WhenQueryIsValid()
    {
        var repository = new InMemoryRegulationRepository();
        var service = new InMemoryRegulationSearchService(repository, repository, CreateQueryExpander());
        var request = new SearchRegulationRequest
        {
            Query = "obligaciones de agentes ALyC",
            Area = "Agentes",
            Limit = 5
        };

        var response = await service.SearchAsync(request, CancellationToken.None);

        response.Query.Should().Be("obligaciones de agentes ALyC");
        response.Results.Should().NotBeEmpty();
        response.Warnings.Should().Contain(warning => warning.Contains("Mock data", StringComparison.OrdinalIgnoreCase));

        var result = response.Results[0];
        result.DocumentId.Should().NotBeNullOrWhiteSpace();
        result.Source.Should().NotBeNullOrWhiteSpace();
        result.Url.Should().NotBeNullOrWhiteSpace();
        result.Citations.Should().NotBeEmpty();
    }

    [Fact]
    public async Task InMemorySearch_ShouldStillWork()
    {
        var repository = new InMemoryRegulationRepository();
        await repository.SaveAsync(
            new()
            {
                Id = "alyc-test",
                Source = "CNV",
                DocumentType = "Texto Ordenado",
                Title = "Agentes",
                Url = "https://example.test",
                Status = "candidate",
                Text = "Obligaciones del agente de liquidación y compensación."
            },
            CancellationToken.None);
        var service = new InMemoryRegulationSearchService(repository, repository, CreateQueryExpander());

        var response = await service.SearchAsync(
            new SearchRegulationRequest { Query = "ALyC obligaciones", Limit = 5 },
            CancellationToken.None);

        response.Query.Should().Be("ALyC obligaciones");
        response.Results.Should().Contain(result => result.DocumentId == "alyc-test");
        response.Warnings.Should().Contain(warning => warning.Contains("Query expansion applied", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InMemorySearch_ShouldHideDuplicateChunksByDefault()
    {
        var repository = new InMemoryRegulationRepository();
        var cnvDocument = CreateDocument("cnv-doc", "CNV", "https://www.cnv.gov.ar/TOC2013.pdf");
        var infolegDocument = CreateDocument("infoleg-doc", "Infoleg", "https://servicios.infoleg.gob.ar/infolegInternet/anexos/1/texact.htm");
        await repository.SaveAsync(cnvDocument, CancellationToken.None);
        await repository.SaveAsync(infolegDocument, CancellationToken.None);
        await repository.ReplaceForDocumentAsync(cnvDocument.Id, [CreateChunk(cnvDocument.Id, "cnv-chunk", duplicateOf: null)], CancellationToken.None);
        await repository.ReplaceForDocumentAsync(infolegDocument.Id, [CreateChunk(infolegDocument.Id, "infoleg-chunk", duplicateOf: "cnv-chunk")], CancellationToken.None);
        var service = new InMemoryRegulationSearchService(repository, repository, CreateQueryExpander());

        var response = await service.SearchAsync(
            new SearchRegulationRequest { Query = "texto unico mercado", Limit = 5 },
            CancellationToken.None);

        response.Results.Should().ContainSingle();
        response.Results[0].DocumentId.Should().Be(cnvDocument.Id);
    }

    [Fact]
    public async Task InMemorySearch_ShouldIncludeDuplicateChunks_WhenRequested()
    {
        var repository = new InMemoryRegulationRepository();
        var cnvDocument = CreateDocument("cnv-doc", "CNV", "https://www.cnv.gov.ar/TOC2013.pdf");
        var infolegDocument = CreateDocument("infoleg-doc", "Infoleg", "https://servicios.infoleg.gob.ar/infolegInternet/anexos/1/texact.htm");
        await repository.SaveAsync(cnvDocument, CancellationToken.None);
        await repository.SaveAsync(infolegDocument, CancellationToken.None);
        await repository.ReplaceForDocumentAsync(cnvDocument.Id, [CreateChunk(cnvDocument.Id, "cnv-chunk", duplicateOf: null)], CancellationToken.None);
        await repository.ReplaceForDocumentAsync(infolegDocument.Id, [CreateChunk(infolegDocument.Id, "infoleg-chunk", duplicateOf: "cnv-chunk")], CancellationToken.None);
        var service = new InMemoryRegulationSearchService(repository, repository, CreateQueryExpander());

        var response = await service.SearchAsync(
            new SearchRegulationRequest { Query = "texto unico mercado", Limit = 5, IncludeDuplicates = true },
            CancellationToken.None);

        response.Results.Should().HaveCount(2);
    }

    private static StaticRegulationQueryExpander CreateQueryExpander() =>
        new(new RegulationAliasesOptions());

    private static RegulationDocument CreateDocument(string id, string source, string url) =>
        new()
        {
            Id = id,
            Source = source,
            DocumentType = "Texto Ordenado",
            Title = $"Document {id}",
            Url = url,
            Status = "candidate",
            Text = "Documento de prueba."
        };

    private static RegulationChunk CreateChunk(string documentId, string id, string? duplicateOf) =>
        new()
        {
            Id = id,
            DocumentId = documentId,
            Article = "Articulo 1",
            ChunkIndex = 0,
            Text = "Texto unico mercado para deduplicacion.",
            ContentHash = "same-hash",
            DuplicateOfChunkId = duplicateOf,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
}
