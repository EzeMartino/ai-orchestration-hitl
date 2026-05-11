using System.Net;
using System.Text;
using System.Text.Json;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Infrastructure.Chunking;
using CnvRegulation.Infrastructure.Ingestion;
using CnvRegulation.Infrastructure.Parsing;
using CnvRegulation.Infrastructure.Repositories;
using CnvRegulation.Infrastructure.Sources;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class SourceDiscoveryDownloadServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [Fact]
    public async Task DiscoverAsync_ShouldCreateCuratedManifest()
    {
        using var testDirectory = TempDirectory.Create();
        var manifestPath = Path.Combine(testDirectory.Path, "source-manifest", "sources.manifest.json");
        var service = new CuratedSourceDiscoveryService(new FixedTimeProvider(new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.Zero)));

        var response = await service.DiscoverAsync(
            new DiscoverSourcesRequest { ManifestPath = manifestPath },
            CancellationToken.None);

        response.SourcesDiscovered.Should().Be(4);
        File.Exists(manifestPath).Should().BeTrue();

        var manifest = JsonSerializer.Deserialize<SourceManifest>(
            await File.ReadAllTextAsync(manifestPath),
            JsonOptions);

        manifest.Should().NotBeNull();
        manifest!.GeneratedAt.Should().Be(new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.Zero));
        manifest.Sources.Should().OnlyContain(source => source.Status == "candidate");
        manifest.Sources.Should().OnlyContain(source => source.RequiresReview);
        manifest.Sources.Should().Contain(source => source.Id == "cnv-toc-2013" && source.FileName.EndsWith(".pdf", StringComparison.Ordinal));
        manifest.Sources.Should().Contain(source => source.Id == "infoleg-rg-622-2013-norma");
        manifest.Sources.Should().Contain(source => source.Id == "infoleg-rg-622-2013-texact");
        manifest.Sources.Should().Contain(source => source.Id == "cnv-marco-regulatorio");
    }

    [Fact]
    public async Task DownloadAsync_ShouldDownloadSourcesAndGenerateMetadataSidecar()
    {
        using var testDirectory = TempDirectory.Create();
        var manifestPath = await WriteManifestAsync(
            testDirectory.Path,
            new SourceManifestItem
            {
                Id = "infoleg-rg-622-2013-texact",
                Source = "Infoleg",
                DocumentType = "Resolución General",
                ResolutionNumber = "622/2013",
                Title = "Resolución General 622/2013 - Texto actualizado",
                Url = "https://example.test/texact.htm",
                FileName = "infoleg-rg-622-2013-texact.html",
                MetadataFileName = "infoleg-rg-622-2013-texact.metadata.json",
                Status = "candidate",
                Priority = "high",
                RequiresReview = true,
                PublicationDate = new DateOnly(2013, 9, 9)
            });
        var outputDirectory = Path.Combine(testDirectory.Path, "sources");
        var service = CreateDownloadService("<html>mock source</html>");

        var response = await service.DownloadAsync(
            new DownloadSourcesRequest
            {
                ManifestPath = manifestPath,
                OutputDirectory = outputDirectory
            },
            CancellationToken.None);

        response.SourcesDownloaded.Should().Be(1);
        response.SourcesSkipped.Should().Be(0);
        File.Exists(Path.Combine(outputDirectory, "infoleg-rg-622-2013-texact.html")).Should().BeTrue();

        using var metadata = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(outputDirectory, "infoleg-rg-622-2013-texact.metadata.json")));
        metadata.RootElement.GetProperty("id").GetString().Should().Be("infoleg-rg-622-2013-texact");
        metadata.RootElement.GetProperty("status").GetString().Should().Be("candidate");
        metadata.RootElement.GetProperty("requiresReview").GetBoolean().Should().BeTrue();
        metadata.RootElement.GetProperty("retrievedAt").GetDateTimeOffset()
            .Should().Be(new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task IngestAsync_ShouldProcessDownloadedPdfFile()
    {
        using var testDirectory = TempDirectory.Create();
        await PdfTestDocumentFactory.WriteSampleRegulationPdfAsync(Path.Combine(testDirectory.Path, "cnv-toc-2013.pdf"));
        await File.WriteAllTextAsync(
            Path.Combine(testDirectory.Path, "cnv-toc-2013.metadata.json"),
            """
            {
              "id": "cnv-toc-2013",
              "source": "CNV",
              "documentType": "Texto Ordenado",
              "title": "Normas CNV N.T. 2013",
              "url": "https://www.cnv.gov.ar/",
              "status": "candidate",
              "requiresReview": true
            }
            """);
        var repository = new InMemoryRegulationRepository();
        var service = new LocalRegulationIngestionService(
            repository,
            repository,
            new LegalStructureRegulationChunker(new LegalStructureDetector()),
            new PlainTextRegulationParser(),
            new HtmlRegulationParser(),
            new PdfPigTextExtractor(),
            new SidecarMetadataReader());

        var response = await service.IngestAsync(
            new IngestRegulationSourceRequest { SourceDirectory = testDirectory.Path },
            CancellationToken.None);

        response.DocumentsIngested.Should().Be(1);
        response.DocumentsSkipped.Should().Be(0);
        response.Warnings.Should().BeEmpty();

        var chunks = await repository.ListByDocumentIdAsync("cnv-toc-2013", CancellationToken.None);
        chunks.Should().HaveCount(2);
        chunks.Should().OnlyContain(chunk => chunk.Metadata.ContainsKey("extractionMethod"));
    }

    [Fact]
    public async Task DownloadAsync_ShouldUseSafeFileNames()
    {
        using var testDirectory = TempDirectory.Create();
        var manifestPath = await WriteManifestAsync(
            testDirectory.Path,
            new SourceManifestItem
            {
                Id = "unsafe-source",
                Source = "CNV",
                DocumentType = "Marco Regulatorio",
                Title = "Unsafe Source",
                Url = "https://example.test/source",
                FileName = "../bad:name.html",
                MetadataFileName = "../bad:name.metadata.json",
                Status = "candidate",
                Priority = "medium",
                RequiresReview = true
            });
        var outputDirectory = Path.Combine(testDirectory.Path, "sources");
        var service = CreateDownloadService("<html>mock source</html>");

        var response = await service.DownloadAsync(
            new DownloadSourcesRequest
            {
                ManifestPath = manifestPath,
                OutputDirectory = outputDirectory
            },
            CancellationToken.None);

        response.SourcesDownloaded.Should().Be(1);
        File.Exists(Path.Combine(outputDirectory, "bad-name.html")).Should().BeTrue();
        File.Exists(Path.Combine(outputDirectory, "bad-name.metadata.json")).Should().BeTrue();
        File.Exists(Path.Combine(testDirectory.Path, "bad:name.html")).Should().BeFalse();
    }

    private static ManifestSourceDownloadService CreateDownloadService(string responseBody)
    {
        var httpClient = new HttpClient(new StaticResponseHandler(responseBody));
        return new ManifestSourceDownloadService(
            httpClient,
            new FixedTimeProvider(new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.Zero)));
    }

    private static async Task<string> WriteManifestAsync(string directory, SourceManifestItem source)
    {
        var manifestPath = Path.Combine(directory, "sources.manifest.json");
        var manifest = new SourceManifest
        {
            GeneratedAt = new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.Zero),
            Sources = [source]
        };

        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
        return manifestPath;
    }

    private sealed class StaticResponseHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "text/html")
            };

            return Task.FromResult(response);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
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
