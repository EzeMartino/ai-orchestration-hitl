using CnvRegulation.Application.Contracts;
using CnvRegulation.Infrastructure.Chunking;
using CnvRegulation.Infrastructure.Ingestion;
using CnvRegulation.Infrastructure.InMemory;
using CnvRegulation.Infrastructure.Repositories;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class LocalRegulationIngestionServiceTests
{
    [Fact]
    public async Task IngestAsync_ShouldLoadTxtFileWithMetadata()
    {
        using var testDirectory = TempSourceDirectory.Create();
        await testDirectory.WriteSourceAsync(
            "cnv-nt-2013.sample.txt",
            CnvRegulationChunkerTests.SampleText,
            CreateMetadataJson("cnv-nt-2013-sample", "Normas CNV N.T. 2013 - Sample"));
        var repository = new InMemoryRegulationRepository();
        var service = CreateService(repository);

        var response = await service.IngestAsync(
            new IngestRegulationSourceRequest { SourceDirectory = testDirectory.Path },
            CancellationToken.None);

        response.DocumentsIngested.Should().Be(1);
        response.DocumentsSkipped.Should().Be(0);
        response.Warnings.Should().BeEmpty();

        var document = await repository.GetByIdAsync("cnv-nt-2013-sample", CancellationToken.None);
        document.Should().NotBeNull();
        document!.Text.Should().Contain("primer artículo de prueba");
        document.Title.Should().Be("Normas CNV N.T. 2013 - Sample");
    }

    [Fact]
    public async Task IngestAsync_ShouldCreateChunksForIngestedDocument()
    {
        using var testDirectory = TempSourceDirectory.Create();
        await testDirectory.WriteSourceAsync(
            "cnv-nt-2013.sample.txt",
            CnvRegulationChunkerTests.SampleText,
            CreateMetadataJson("cnv-nt-2013-sample", "Normas CNV N.T. 2013 - Sample"));
        var repository = new InMemoryRegulationRepository();
        var service = CreateService(repository);

        var response = await service.IngestAsync(
            new IngestRegulationSourceRequest { SourceDirectory = testDirectory.Path },
            CancellationToken.None);

        var chunks = await repository.ListByDocumentIdAsync("cnv-nt-2013-sample", CancellationToken.None);

        response.DocumentsIngested.Should().Be(1);
        chunks.Should().HaveCount(3);
        chunks[0].Article.Should().Be("Artículo 1");
        chunks[0].Chapter.Should().Be("Capítulo I");
        chunks[2].Article.Should().Be("Artículo 3");
        chunks[2].Chapter.Should().Be("Capítulo II");
    }

    [Fact]
    public async Task IngestAsync_ShouldLoadHtmlFileWithMetadata()
    {
        using var testDirectory = TempSourceDirectory.Create();
        await testDirectory.WriteSourceAsync(
            "rg-990-2024.sample.html",
            "<html><body><h1>RG 990</h1><p>Mock HTML regulatory text for market conduct.</p></body></html>",
            CreateMetadataJson("rg-990-2024-sample", "Resolucion General CNV 990/2024 - Sample"));
        var repository = new InMemoryRegulationRepository();
        var service = CreateService(repository);

        var response = await service.IngestAsync(
            new IngestRegulationSourceRequest { SourceDirectory = testDirectory.Path },
            CancellationToken.None);

        response.DocumentsIngested.Should().Be(1);
        response.DocumentsSkipped.Should().Be(0);

        var document = await repository.GetByIdAsync("rg-990-2024-sample", CancellationToken.None);
        document.Should().NotBeNull();
        document!.Text.Should().Contain("Mock HTML regulatory text");
        document.Text.Should().NotContain("<p>");
    }

    [Fact]
    public async Task IngestAsync_ShouldSkipFile_WhenMetadataIsMissing()
    {
        using var testDirectory = TempSourceDirectory.Create();
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(testDirectory.Path, "missing-metadata.sample.txt"),
            "Mock regulatory text without sidecar metadata.");
        var repository = new InMemoryRegulationRepository();
        var service = CreateService(repository);

        var response = await service.IngestAsync(
            new IngestRegulationSourceRequest { SourceDirectory = testDirectory.Path },
            CancellationToken.None);

        response.DocumentsIngested.Should().Be(0);
        response.DocumentsSkipped.Should().Be(1);
        response.Warnings.Should().Contain(warning => warning.Contains("metadata file is missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IngestAsync_ShouldReturnWarning_WhenDirectoryDoesNotExist()
    {
        var sourceDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var repository = new InMemoryRegulationRepository();
        var service = CreateService(repository);

        var response = await service.IngestAsync(
            new IngestRegulationSourceRequest { SourceDirectory = sourceDirectory },
            CancellationToken.None);

        response.DocumentsIngested.Should().Be(0);
        response.DocumentsSkipped.Should().Be(0);
        response.Warnings.Should().Contain(warning => warning.Contains("does not exist", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IngestAsync_ShouldSkipFile_WhenMetadataIsInvalid()
    {
        using var testDirectory = TempSourceDirectory.Create();
        var sourceFile = System.IO.Path.Combine(testDirectory.Path, "invalid-metadata.sample.txt");
        var metadataFile = System.IO.Path.Combine(testDirectory.Path, "invalid-metadata.sample.metadata.json");
        await File.WriteAllTextAsync(sourceFile, "Mock regulatory text.");
        await File.WriteAllTextAsync(metadataFile, """{ "id": "invalid-metadata-sample" }""");
        var repository = new InMemoryRegulationRepository();
        var service = CreateService(repository);

        var response = await service.IngestAsync(
            new IngestRegulationSourceRequest { SourceDirectory = testDirectory.Path },
            CancellationToken.None);

        response.DocumentsIngested.Should().Be(0);
        response.DocumentsSkipped.Should().Be(1);
        response.Warnings.Should().Contain(warning => warning.Contains("missing required fields", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnChunkLevelResult_WhenChunksExist()
    {
        using var testDirectory = TempSourceDirectory.Create();
        await testDirectory.WriteSourceAsync(
            "cnv-nt-2013.sample.txt",
            CnvRegulationChunkerTests.SampleText,
            CreateMetadataJson("cnv-nt-2013-sample", "Normas CNV N.T. 2013 - Sample"));
        var repository = new InMemoryRegulationRepository();
        var ingestionService = CreateService(repository);
        await ingestionService.IngestAsync(
            new IngestRegulationSourceRequest { SourceDirectory = testDirectory.Path },
            CancellationToken.None);
        var searchService = new InMemoryRegulationSearchService(repository, repository);

        var response = await searchService.SearchAsync(
            new SearchRegulationRequest
            {
                Query = "primer artículo de prueba",
                Limit = 5
            },
            CancellationToken.None);

        var result = response.Results.Should()
            .ContainSingle(result => result.ChunkId == "cnv-nt-2013-sample-articulo-1")
            .Which;

        result.DocumentId.Should().Be("cnv-nt-2013-sample");
        result.Title.Should().Be("Normas CNV N.T. 2013 - Sample");
        result.Chapter.Should().Be("Capítulo I");
        result.Article.Should().Be("Artículo 1");
        result.Citations.Should().ContainSingle();
        result.Citations[0].QuotedText.Should().Contain("primer artículo de prueba");
    }

    private static LocalRegulationIngestionService CreateService(InMemoryRegulationRepository repository) =>
        new(
            repository,
            repository,
            new CnvRegulationChunker(new LegalStructureDetector()),
            new PlainTextRegulationParser(),
            new HtmlRegulationParser(),
            new SidecarMetadataReader());

    private static string CreateMetadataJson(string id, string title) =>
        $$"""
        {
          "id": "{{id}}",
          "source": "CNV",
          "documentType": "Texto Ordenado",
          "resolutionNumber": "622/2013",
          "title": "{{title}}",
          "publicationDate": "2013-09-09",
          "effectiveDate": "2013-09-09",
          "url": "https://www.cnv.gov.ar/",
          "status": "mock"
        }
        """;

    private sealed class TempSourceDirectory : IDisposable
    {
        private TempSourceDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempSourceDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);

            return new TempSourceDirectory(path);
        }

        public async Task WriteSourceAsync(string sourceFileName, string sourceText, string metadataJson)
        {
            var sourceFile = System.IO.Path.Combine(Path, sourceFileName);
            var metadataFile = System.IO.Path.Combine(
                Path,
                $"{System.IO.Path.GetFileNameWithoutExtension(sourceFileName)}.metadata.json");

            await File.WriteAllTextAsync(sourceFile, sourceText);
            await File.WriteAllTextAsync(metadataFile, metadataJson);
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
