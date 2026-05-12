using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.Embeddings;
using CnvRegulation.Infrastructure.Repositories;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class EmbeddingServiceTests
{
    [Fact]
    public async Task FakeEmbeddingGenerator_ShouldReturnDeterministicVector()
    {
        var generator = new DeterministicFakeEmbeddingGenerator(new EmbeddingOptions { Dimensions = 32 });

        var first = await generator.GenerateAsync("ALyC mercado autorizado", CancellationToken.None);
        var second = await generator.GenerateAsync("ALyC mercado autorizado", CancellationToken.None);

        first.Model.Should().Be("fake-deterministic");
        first.Dimensions.Should().Be(32);
        first.Vector.Should().Equal(second.Vector);
        first.Vector.Should().HaveCount(32);
    }

    [Fact]
    public async Task GenerateEmbeddings_ShouldPersistEmbeddingsForSearchableChunks()
    {
        var repository = new InMemoryRegulationRepository();
        var service = CreateService(repository);
        var document = CreateDocument();
        var chunk = CreateChunk(document.Id, "chunk-1", "mercado autorizado", duplicateOfChunkId: null);

        await repository.SaveAsync(document, CancellationToken.None);
        await repository.ReplaceForDocumentAsync(document.Id, [chunk], CancellationToken.None);

        var response = await service.GenerateMissingEmbeddingsAsync(
            new GenerateEmbeddingsRequest { Provider = "fake", Dimensions = 32 },
            CancellationToken.None);
        var saved = await repository.ListByDocumentIdAsync(document.Id, CancellationToken.None);

        response.Generated.Should().Be(1);
        saved[0].Embedding.Should().NotBeNull();
        saved[0].Embedding!.Should().HaveCount(32);
    }

    [Fact]
    public async Task GenerateEmbeddings_ShouldSkipDuplicateChunksByDefault()
    {
        var repository = new InMemoryRegulationRepository();
        var service = CreateService(repository);
        var document = CreateDocument();

        await repository.SaveAsync(document, CancellationToken.None);
        await repository.ReplaceForDocumentAsync(
            document.Id,
            [CreateChunk(document.Id, "chunk-1", "mercado", duplicateOfChunkId: "canonical")],
            CancellationToken.None);

        var response = await service.GenerateMissingEmbeddingsAsync(
            new GenerateEmbeddingsRequest { Provider = "fake", Dimensions = 32 },
            CancellationToken.None);

        response.Generated.Should().Be(0);
        response.SkippedDuplicates.Should().Be(1);
    }

    [Fact]
    public async Task GenerateEmbeddings_ShouldSkipNonSearchableChunksByDefault()
    {
        var repository = new InMemoryRegulationRepository();
        var service = CreateService(repository);
        var document = CreateDocument(searchable: false);

        await repository.SaveAsync(document, CancellationToken.None);
        await repository.ReplaceForDocumentAsync(
            document.Id,
            [CreateChunk(document.Id, "chunk-1", "mercado", duplicateOfChunkId: null)],
            CancellationToken.None);

        var response = await service.GenerateMissingEmbeddingsAsync(
            new GenerateEmbeddingsRequest { Provider = "fake", Dimensions = 32 },
            CancellationToken.None);

        response.Generated.Should().Be(0);
        response.SkippedNonSearchable.Should().Be(1);
    }

    private static RegulationEmbeddingService CreateService(InMemoryRegulationRepository repository)
    {
        var options = new EmbeddingOptions { Dimensions = 32 };
        var httpClient = new HttpClient();

        return new RegulationEmbeddingService(
            repository,
            repository,
            repository,
            new DeterministicFakeEmbeddingGenerator(options),
            new OpenAiEmbeddingGenerator(httpClient, options),
            options,
            httpClient,
            TimeProvider.System);
    }

    private static RegulationDocument CreateDocument(bool searchable = true) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Source = "CNV",
            DocumentType = "Texto Ordenado",
            Title = "Documento test",
            Url = "https://www.cnv.gov.ar/",
            Status = "candidate",
            Text = "Documento test",
            Metadata = searchable
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["searchable"] = "false"
                }
        };

    private static RegulationChunk CreateChunk(
        string documentId,
        string chunkId,
        string text,
        string? duplicateOfChunkId) =>
        new()
        {
            Id = chunkId,
            DocumentId = documentId,
            Article = "Articulo 1",
            ChunkIndex = 0,
            Text = text,
            DuplicateOfChunkId = duplicateOfChunkId,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
}
