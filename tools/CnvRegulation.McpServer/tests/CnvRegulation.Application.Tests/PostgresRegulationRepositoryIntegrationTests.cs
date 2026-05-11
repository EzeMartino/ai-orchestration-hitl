using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.Chunking;
using CnvRegulation.Infrastructure.Ingestion;
using CnvRegulation.Infrastructure.InMemory;
using CnvRegulation.Infrastructure.Parsing;
using CnvRegulation.Infrastructure.Persistence;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class PostgresRegulationRepositoryIntegrationTests
{
    [PostgresIntegrationFact]
    public async Task PostgresRepository_ShouldSaveAndRetrieveDocument()
    {
        var repository = await CreateRepositoryAsync();
        var document = CreateDocument();

        await repository.SaveAsync(document, CancellationToken.None);

        var saved = await repository.GetByIdAsync(document.Id, CancellationToken.None);
        saved.Should().NotBeNull();
        saved!.Source.Should().Be("Infoleg");
        saved.DocumentType.Should().Be("Resolucion General");
        saved.ResolutionNumber.Should().Be("622/2013");
        saved.RequiresReview.Should().BeTrue();
        saved.RetrievedAt.Should().Be(document.RetrievedAt);
        saved.Metadata.Should().ContainKey("testRun");
    }

    [PostgresIntegrationFact]
    public async Task PostgresRepository_ShouldSaveAndRetrieveChunks()
    {
        var repository = await CreateRepositoryAsync();
        var document = CreateDocument();
        var chunks = CreateChunks(document.Id);

        await repository.SaveAsync(document, CancellationToken.None);
        await repository.ReplaceForDocumentAsync(document.Id, chunks, CancellationToken.None);

        var saved = await repository.ListByDocumentIdAsync(document.Id, CancellationToken.None);
        saved.Should().HaveCount(2);
        saved[0].Article.Should().Be("Articulo 98764");
        saved[1].Metadata.Should().ContainKey("article");
    }

    [PostgresIntegrationFact]
    public async Task PostgresRepository_ShouldReplaceChunksOnReingest()
    {
        var repository = await CreateRepositoryAsync();
        var document = CreateDocument();
        var chunks = CreateChunks(document.Id);

        await repository.SaveAsync(document, CancellationToken.None);
        await repository.ReplaceForDocumentAsync(document.Id, chunks, CancellationToken.None);
        await repository.ReplaceForDocumentAsync(document.Id, [chunks[0]], CancellationToken.None);

        var saved = await repository.ListByDocumentIdAsync(document.Id, CancellationToken.None);
        saved.Should().ContainSingle();
        saved[0].Id.Should().Be(chunks[0].Id);
    }

    [PostgresIntegrationFact]
    public async Task PostgresRepository_ShouldFindArticleByArticleNumber()
    {
        var repository = await CreateRepositoryAsync();
        var document = CreateDocument();
        var chunks = CreateChunks(document.Id);

        await repository.SaveAsync(document, CancellationToken.None);
        await repository.ReplaceForDocumentAsync(document.Id, chunks, CancellationToken.None);
        var articleService = new InMemoryRegulationArticleService(repository, repository);

        var response = await articleService.GetArticleAsync(
            new GetRegulationArticleRequest { Article = "Articulo 98765" },
            CancellationToken.None);

        response.Citation.Source.Should().Be("Infoleg");
        response.Citation.ResolutionNumber.Should().Be("622/2013");
        response.Citation.Article.Should().Be("Articulo 98765");
        response.Warnings.Should().Contain(["candidate source", "requires review"]);
    }

    [PostgresIntegrationFact]
    public async Task PostgresRepository_ShouldSearchChunksBeforeDocuments()
    {
        var repository = await CreateRepositoryAsync();
        var document = CreateDocument();
        var chunks = CreateChunks(document.Id);

        await repository.SaveAsync(document, CancellationToken.None);
        await repository.ReplaceForDocumentAsync(document.Id, chunks, CancellationToken.None);
        var searchService = new InMemoryRegulationSearchService(repository, repository);

        var uniqueQuery = $"unique chunk search text {document.Id}";
        var response = await searchService.SearchAsync(
            new SearchRegulationRequest { Query = uniqueQuery, Limit = 5 },
            CancellationToken.None);

        response.Results.Should().NotBeEmpty();
        response.Results[0].ChunkId.Should().Be($"{document.Id}-articulo-2");
        response.Results[0].Article.Should().Be("Articulo 98765");
    }

    [PostgresIntegrationFact]
    public async Task PostgresSearch_ShouldReturnChunkMatches()
    {
        var services = await CreateSearchServicesAsync();
        var document = CreateSearchDocument("Infoleg", "622/2013", requiresReview: true);
        var token = CreateSearchToken();
        var chunks = CreateSearchChunks(document.Id, token);

        await services.Repository.SaveAsync(document, CancellationToken.None);
        await services.Repository.ReplaceForDocumentAsync(document.Id, chunks, CancellationToken.None);

        var response = await services.SearchService.SearchAsync(
            new SearchRegulationRequest { Query = token, Limit = 5 },
            CancellationToken.None);

        response.Results.Should().NotBeEmpty();
        response.Results[0].DocumentId.Should().Be(document.Id);
        response.Results[0].ChunkId.Should().Be($"{document.Id}-search-1");
        response.Results[0].Citations.Should().NotBeEmpty();
    }

    [PostgresIntegrationFact]
    public async Task PostgresSearch_ShouldRankMoreRelevantChunksFirst()
    {
        var services = await CreateSearchServicesAsync();
        var document = CreateSearchDocument("Infoleg", "622/2013", requiresReview: true);
        var token = CreateSearchToken();
        var chunks = CreateSearchChunks(document.Id, token);

        await services.Repository.SaveAsync(document, CancellationToken.None);
        await services.Repository.ReplaceForDocumentAsync(document.Id, chunks, CancellationToken.None);

        var response = await services.SearchService.SearchAsync(
            new SearchRegulationRequest { Query = token, Limit = 5 },
            CancellationToken.None);

        response.Results.Should().HaveCountGreaterThanOrEqualTo(2);
        response.Results[0].ChunkId.Should().Be($"{document.Id}-search-1");
        response.Results[0].Score.Should().BeGreaterThan(response.Results[1].Score);
    }

    [PostgresIntegrationFact]
    public async Task PostgresSearch_ShouldFilterBySource()
    {
        var services = await CreateSearchServicesAsync();
        var document = CreateSearchDocument("Infoleg", "622/2013", requiresReview: true);
        var otherDocument = CreateSearchDocument("CNV", "999/2026", requiresReview: true);
        var token = CreateSearchToken();

        await services.Repository.SaveAsync(document, CancellationToken.None);
        await services.Repository.ReplaceForDocumentAsync(document.Id, CreateSearchChunks(document.Id, token), CancellationToken.None);
        await services.Repository.SaveAsync(otherDocument, CancellationToken.None);
        await services.Repository.ReplaceForDocumentAsync(otherDocument.Id, CreateSearchChunks(otherDocument.Id, token), CancellationToken.None);

        var response = await services.SearchService.SearchAsync(
            new SearchRegulationRequest { Query = token, Source = "Infoleg", Limit = 10 },
            CancellationToken.None);

        response.Results.Should().NotBeEmpty();
        response.Results.Should().OnlyContain(result => result.Source == "Infoleg");
    }

    [PostgresIntegrationFact]
    public async Task PostgresSearch_ShouldFilterByResolutionNumber()
    {
        var services = await CreateSearchServicesAsync();
        var document = CreateSearchDocument("Infoleg", "622/2013", requiresReview: true);
        var otherDocument = CreateSearchDocument("Infoleg", "999/2026", requiresReview: true);
        var token = CreateSearchToken();

        await services.Repository.SaveAsync(document, CancellationToken.None);
        await services.Repository.ReplaceForDocumentAsync(document.Id, CreateSearchChunks(document.Id, token), CancellationToken.None);
        await services.Repository.SaveAsync(otherDocument, CancellationToken.None);
        await services.Repository.ReplaceForDocumentAsync(otherDocument.Id, CreateSearchChunks(otherDocument.Id, token), CancellationToken.None);

        var response = await services.SearchService.SearchAsync(
            new SearchRegulationRequest { Query = token, ResolutionNumber = "622/2013", Limit = 10 },
            CancellationToken.None);

        response.Results.Should().NotBeEmpty();
        response.Results.Should().OnlyContain(result =>
            result.Citations[0].ResolutionNumber == "622/2013");
    }

    [PostgresIntegrationFact]
    public async Task PostgresSearch_ShouldFilterByRequiresReview()
    {
        var services = await CreateSearchServicesAsync();
        var reviewDocument = CreateSearchDocument("Infoleg", "622/2013", requiresReview: true);
        var approvedDocument = CreateSearchDocument("Infoleg", "623/2013", requiresReview: false);
        var token = CreateSearchToken();

        await services.Repository.SaveAsync(reviewDocument, CancellationToken.None);
        await services.Repository.ReplaceForDocumentAsync(reviewDocument.Id, CreateSearchChunks(reviewDocument.Id, token), CancellationToken.None);
        await services.Repository.SaveAsync(approvedDocument, CancellationToken.None);
        await services.Repository.ReplaceForDocumentAsync(approvedDocument.Id, CreateSearchChunks(approvedDocument.Id, token), CancellationToken.None);

        var response = await services.SearchService.SearchAsync(
            new SearchRegulationRequest { Query = token, RequiresReview = false, Limit = 10 },
            CancellationToken.None);

        response.Results.Should().NotBeEmpty();
        response.Results.Should().OnlyContain(result => result.DocumentId == approvedDocument.Id);
    }

    [PostgresIntegrationFact]
    public async Task PostgresSearch_ShouldFallbackToDocumentSearch_WhenNoChunkMatches()
    {
        var services = await CreateSearchServicesAsync();
        var token = CreateSearchToken();
        var document = CreateSearchDocument("Infoleg", "622/2013", requiresReview: true, documentOnlyToken: token);

        await services.Repository.SaveAsync(document, CancellationToken.None);
        await services.Repository.ReplaceForDocumentAsync(
            document.Id,
            CreateSearchChunks(document.Id, CreateSearchToken()),
            CancellationToken.None);

        var response = await services.SearchService.SearchAsync(
            new SearchRegulationRequest { Query = token, Limit = 5 },
            CancellationToken.None);

        response.Results.Should().NotBeEmpty();
        response.Results[0].DocumentId.Should().Be(document.Id);
        response.Results[0].ChunkId.Should().Be($"{document.Id}-document");
        response.Results[0].Citations.Should().NotBeEmpty();
    }

    [PostgresIntegrationFact]
    public async Task PostgresIngest_ShouldPersistPdfDocumentAndChunks()
    {
        using var testDirectory = TempDirectory.Create();
        var documentId = $"test-pdf-{Guid.NewGuid():N}";
        await PdfTestDocumentFactory.WriteSampleRegulationPdfAsync(Path.Combine(testDirectory.Path, "sample.pdf"));
        await File.WriteAllTextAsync(
            Path.Combine(testDirectory.Path, "sample.metadata.json"),
            CreateMetadataJson(documentId));
        var repository = await CreateRepositoryAsync();
        var ingestionService = new LocalRegulationIngestionService(
            repository,
            repository,
            new LegalStructureRegulationChunker(new LegalStructureDetector()),
            new PlainTextRegulationParser(),
            new HtmlRegulationParser(),
            new PdfPigTextExtractor(),
            new SidecarMetadataReader());

        var response = await ingestionService.IngestAsync(
            new IngestRegulationSourceRequest { SourceDirectory = testDirectory.Path },
            CancellationToken.None);

        response.DocumentsIngested.Should().Be(1);
        var savedDocument = await repository.GetByIdAsync(documentId, CancellationToken.None);
        var savedChunks = await repository.ListByDocumentIdAsync(documentId, CancellationToken.None);
        var searchService = new PostgresRegulationSearchService(new RegulationDbConnectionFactory(
            RegulationDbOptions.Create("Postgres", Environment.GetEnvironmentVariable("CNV_REGULATION_DB_CONNECTION_STRING"))));
        var search = await searchService.SearchAsync(
            new SearchRegulationRequest { Query = "prueba PDF", Limit = 5 },
            CancellationToken.None);

        savedDocument.Should().NotBeNull();
        savedDocument!.Metadata.Should().Contain("extractionMethod", "PdfPig");
        savedChunks.Should().HaveCount(2);
        savedChunks.Should().OnlyContain(chunk => chunk.Metadata.ContainsKey("extractionMethod"));
        search.Results.Should().Contain(result => result.DocumentId == documentId);
    }

    private static async Task<PostgresRegulationRepository> CreateRepositoryAsync()
    {
        var options = RegulationDbOptions.Create(
            "Postgres",
            Environment.GetEnvironmentVariable("CNV_REGULATION_DB_CONNECTION_STRING"));
        var connectionFactory = new RegulationDbConnectionFactory(options);
        var migrator = new PostgresRegulationDatabaseMigrator(connectionFactory);

        await migrator.MigrateAsync(CancellationToken.None);

        return new PostgresRegulationRepository(connectionFactory);
    }

    private static async Task<(PostgresRegulationRepository Repository, PostgresRegulationSearchService SearchService)> CreateSearchServicesAsync()
    {
        var options = RegulationDbOptions.Create(
            "Postgres",
            Environment.GetEnvironmentVariable("CNV_REGULATION_DB_CONNECTION_STRING"));
        var connectionFactory = new RegulationDbConnectionFactory(options);
        var migrator = new PostgresRegulationDatabaseMigrator(connectionFactory);

        await migrator.MigrateAsync(CancellationToken.None);

        return (
            new PostgresRegulationRepository(connectionFactory),
            new PostgresRegulationSearchService(connectionFactory));
    }

    private static RegulationDocument CreateDocument()
    {
        var id = $"test-rg-622-{Guid.NewGuid():N}";

        return new RegulationDocument
        {
            Id = id,
            Source = "Infoleg",
            DocumentType = "Resolucion General",
            ResolutionNumber = "622/2013",
            Title = "Resolucion General 622/2013 - Integration Test",
            PublicationDate = new DateOnly(2013, 9, 9),
            EffectiveDate = new DateOnly(2013, 9, 9),
            Url = "https://servicios.infoleg.gob.ar/test",
            Status = "candidate",
            RequiresReview = true,
            RetrievedAt = DateTimeOffset.Parse("2026-05-08T00:00:00Z"),
            Text = "Integration test document text.",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["testRun"] = "true"
            }
        };
    }

    private static IReadOnlyList<RegulationChunk> CreateChunks(string documentId) =>
    [
        new()
        {
            Id = $"{documentId}-articulo-1",
            DocumentId = documentId,
            Title = "Titulo I",
            Chapter = "Capitulo I",
            Article = "Articulo 98764",
            ChunkIndex = 0,
            Text = "Articulo 1 test text.",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["article"] = "Articulo 98764"
            }
        },
        new()
        {
            Id = $"{documentId}-articulo-2",
            DocumentId = documentId,
            Title = "Titulo I",
            Chapter = "Capitulo I",
            Article = "Articulo 98765",
            ChunkIndex = 1,
            Text = $"Articulo 98765 unique chunk search text {documentId}.",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["article"] = "Articulo 98765"
            }
        }
    ];

    private static RegulationDocument CreateSearchDocument(
        string source,
        string resolutionNumber,
        bool requiresReview,
        string? documentOnlyToken = null)
    {
        var id = $"test-search-{Guid.NewGuid():N}";

        return new RegulationDocument
        {
            Id = id,
            Source = source,
            DocumentType = "Resolucion General",
            ResolutionNumber = resolutionNumber,
            Title = $"Search integration document {id}",
            PublicationDate = new DateOnly(2026, 5, 8),
            Url = "https://example.test/search",
            Status = requiresReview ? "candidate" : "reviewed",
            RequiresReview = requiresReview,
            Text = string.IsNullOrWhiteSpace(documentOnlyToken)
                ? $"Document text for {id}."
                : $"Document fallback text {documentOnlyToken} for {id}.",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["testRun"] = "true"
            }
        };
    }

    private static IReadOnlyList<RegulationChunk> CreateSearchChunks(string documentId, string token) =>
    [
        new()
        {
            Id = $"{documentId}-search-1",
            DocumentId = documentId,
            Title = "Titulo Search",
            Chapter = "Capitulo Search",
            Article = "Articulo 101",
            ChunkIndex = 0,
            Text = $"{token} {token} {token} mercado regulatorio.",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["article"] = "Articulo 101"
            }
        },
        new()
        {
            Id = $"{documentId}-search-2",
            DocumentId = documentId,
            Title = "Titulo Search",
            Chapter = "Capitulo Search",
            Article = "Articulo 102",
            ChunkIndex = 1,
            Text = $"{token} mercado.",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["article"] = "Articulo 102"
            }
        }
    ];

    private static string CreateSearchToken() =>
        $"zafiro{Guid.NewGuid():N}";

    private static string CreateMetadataJson(string id) =>
        $$"""
        {
          "id": "{{id}}",
          "source": "CNV",
          "documentType": "Texto Ordenado",
          "resolutionNumber": "622/2013",
          "title": "Normas CNV N.T. 2013 - PDF Integration Test",
          "publicationDate": "2013-09-09",
          "effectiveDate": "2013-09-09",
          "url": "https://www.cnv.gov.ar/",
          "status": "candidate",
          "requiresReview": true
        }
        """;

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

internal sealed class PostgresIntegrationFactAttribute : FactAttribute
{
    public PostgresIntegrationFactAttribute()
    {
        if (!ShouldRunIntegrationTests())
        {
            Skip = "Set CNV_REGULATION_RUN_INTEGRATION_TESTS=true and CNV_REGULATION_DB_CONNECTION_STRING to run PostgreSQL integration tests.";
        }
    }

    private static bool ShouldRunIntegrationTests() =>
        string.Equals(
            Environment.GetEnvironmentVariable("CNV_REGULATION_RUN_INTEGRATION_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CNV_REGULATION_DB_CONNECTION_STRING"));
}
