using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.Embeddings;
using CnvRegulation.Infrastructure.Repositories;
using FluentAssertions;
using System.Net;

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

    [Fact]
    public async Task GenerateEmbeddings_ShouldDryRunWithoutPersistingEmbeddings()
    {
        var repository = new InMemoryRegulationRepository();
        var service = CreateService(repository);
        var document = CreateDocument();

        await repository.SaveAsync(document, CancellationToken.None);
        await repository.ReplaceForDocumentAsync(
            document.Id,
            [CreateChunk(document.Id, "chunk-1", "mercado autorizado", duplicateOfChunkId: null)],
            CancellationToken.None);

        var response = await service.GenerateMissingEmbeddingsAsync(
            new GenerateEmbeddingsRequest { Provider = "fake", Dimensions = 32, DryRun = true },
            CancellationToken.None);
        var saved = await repository.ListByDocumentIdAsync(document.Id, CancellationToken.None);

        response.EligibleChunks.Should().Be(1);
        response.Generated.Should().Be(0);
        response.EstimatedTokenCount.Should().BeGreaterThan(0);
        response.Warnings.Should().Contain(warning => warning.Contains("Dry run", StringComparison.OrdinalIgnoreCase));
        saved[0].Embedding.Should().BeNull();
    }

    [Fact]
    public async Task GenerateEmbeddings_ShouldReportAlreadyEmbeddedChunksForResume()
    {
        var repository = new InMemoryRegulationRepository();
        var service = CreateService(repository);
        var document = CreateDocument();

        await repository.SaveAsync(document, CancellationToken.None);
        await repository.ReplaceForDocumentAsync(
            document.Id,
            [CreateChunk(document.Id, "chunk-1", "mercado autorizado", duplicateOfChunkId: null)],
            CancellationToken.None);
        await service.GenerateMissingEmbeddingsAsync(
            new GenerateEmbeddingsRequest { Provider = "fake", Dimensions = 32 },
            CancellationToken.None);

        var response = await service.GenerateMissingEmbeddingsAsync(
            new GenerateEmbeddingsRequest { Provider = "fake", Dimensions = 32, OnlyMissing = true },
            CancellationToken.None);

        response.Generated.Should().Be(0);
        response.AlreadyEmbedded.Should().Be(1);
        response.Warnings.Should().Contain(warning => warning.Contains("Resume mode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateEmbeddings_ShouldRespectLimit()
    {
        var repository = new InMemoryRegulationRepository();
        var service = CreateService(repository);
        var document = CreateDocument();

        await repository.SaveAsync(document, CancellationToken.None);
        await repository.ReplaceForDocumentAsync(
            document.Id,
            [
                CreateChunk(document.Id, "chunk-1", "mercado autorizado", duplicateOfChunkId: null),
                CreateChunk(document.Id, "chunk-2", "oferta publica", duplicateOfChunkId: null)
            ],
            CancellationToken.None);

        var response = await service.GenerateMissingEmbeddingsAsync(
            new GenerateEmbeddingsRequest { Provider = "fake", Dimensions = 32, Limit = 1 },
            CancellationToken.None);
        var saved = await repository.ListByDocumentIdAsync(document.Id, CancellationToken.None);

        response.Generated.Should().Be(1);
        saved.Count(chunk => chunk.Embedding is not null).Should().Be(1);
    }

    [Fact]
    public async Task GenerateEmbeddings_ShouldSkipTooLongChunksAndWriteFailedReport()
    {
        using var testDirectory = TempDirectory.Create();
        var reportPath = Path.Combine(testDirectory.Path, "failed-embeddings.json");
        var repository = new InMemoryRegulationRepository();
        var service = CreateService(repository);
        var document = CreateDocument();

        await repository.SaveAsync(document, CancellationToken.None);
        await repository.ReplaceForDocumentAsync(
            document.Id,
            [CreateChunk(document.Id, "chunk-long", new string('x', 64), duplicateOfChunkId: null)],
            CancellationToken.None);

        var response = await service.GenerateMissingEmbeddingsAsync(
            new GenerateEmbeddingsRequest
            {
                Provider = "fake",
                Dimensions = 32,
                MaxInputCharacters = 32,
                FailedReportPath = reportPath
            },
            CancellationToken.None);

        response.Generated.Should().Be(0);
        response.Failed.Should().Be(1);
        response.FailedItems.Should().ContainSingle().Which.Status.Should().Be("skipped_too_long");
        response.FailedReportPath.Should().Be(Path.GetFullPath(reportPath));
        File.Exists(reportPath).Should().BeTrue();
        var reportJson = await File.ReadAllTextAsync(reportPath);
        reportJson.Should().Contain("chunk-long");
        reportJson.Should().Contain("skipped_too_long");
        var saved = await repository.ListByDocumentIdAsync(document.Id, CancellationToken.None);
        saved[0].Metadata.Should().Contain("embeddingStatus", "skipped_too_long");
    }

    [Fact]
    public async Task GenerateEmbeddings_ShouldRetryTransientOpenAiFailures()
    {
        var repository = new InMemoryRegulationRepository();
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            CreateEmbeddingResponse());
        var service = CreateOpenAiService(repository, handler);
        var document = CreateDocument();

        await repository.SaveAsync(document, CancellationToken.None);
        await repository.ReplaceForDocumentAsync(
            document.Id,
            [CreateChunk(document.Id, "chunk-1", "mercado autorizado", duplicateOfChunkId: null)],
            CancellationToken.None);

        var response = await service.GenerateMissingEmbeddingsAsync(
            new GenerateEmbeddingsRequest { Provider = "OpenAI", BatchSize = 1 },
            CancellationToken.None);

        response.Generated.Should().Be(1);
        response.Failed.Should().Be(0);
        handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task GenerateEmbeddings_ShouldNotRetryBadRequestFailures()
    {
        var repository = new InMemoryRegulationRepository();
        var handler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.BadRequest));
        var service = CreateOpenAiService(repository, handler);
        var document = CreateDocument();

        await repository.SaveAsync(document, CancellationToken.None);
        await repository.ReplaceForDocumentAsync(
            document.Id,
            [CreateChunk(document.Id, "chunk-1", "mercado autorizado", duplicateOfChunkId: null)],
            CancellationToken.None);

        var response = await service.GenerateMissingEmbeddingsAsync(
            new GenerateEmbeddingsRequest { Provider = "OpenAI" },
            CancellationToken.None);

        response.Generated.Should().Be(0);
        response.Failed.Should().Be(1);
        response.FailedItems.Should().ContainSingle().Which.Reason.Should().Contain("400");
        handler.Calls.Should().Be(1);
        var saved = await repository.ListByDocumentIdAsync(document.Id, CancellationToken.None);
        saved[0].Metadata.Should().Contain("embeddingStatus", "failed");
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

    private static RegulationEmbeddingService CreateOpenAiService(
        InMemoryRegulationRepository repository,
        HttpMessageHandler handler)
    {
        var options = new EmbeddingOptions
        {
            Provider = "OpenAI",
            Model = "text-embedding-3-small",
            Dimensions = 32,
            OpenAiApiKey = "test-key"
        };
        var httpClient = new HttpClient(handler);

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

    private static HttpResponseMessage CreateEmbeddingResponse()
    {
        var values = string.Join(',', Enumerable.Repeat("0.1", 32));

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                {
                  "model": "text-embedding-3-small",
                  "data": [
                    {
                      "embedding": [{{values}}]
                    }
                  ],
                  "usage": {
                    "total_tokens": 4
                  }
                }
                """)
        };
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

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> queue = new(responses);

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(queue.Count > 0
                ? queue.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);

            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
