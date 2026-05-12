using System.Net;
using System.Text;
using System.Text.Json;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Infrastructure.Sources;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class InfolegLinkDiscoveryServiceTests
{
    private static readonly Uri BaseUri = new("https://servicios.infoleg.gob.ar/infolegInternet/anexos/215000-219999/219405/norma.htm");

    [Fact]
    public void InfolegLinkDiscovery_ShouldExtractSameDomainLinks()
    {
        var extractor = new InfolegLinkExtractor();

        var result = extractor.Extract(
            """<a href="https://servicios.infoleg.gob.ar/infolegInternet/anexos/215000-219999/219405/texact.htm">texact</a>""",
            BaseUri);

        result.Candidates.Should().ContainSingle(candidate =>
            candidate.Url == "https://servicios.infoleg.gob.ar/infolegInternet/anexos/215000-219999/219405/texact.htm");
    }

    [Fact]
    public void InfolegLinkDiscovery_ShouldRejectExternalLinks()
    {
        var extractor = new InfolegLinkExtractor();

        var result = extractor.Extract(
            """<a href="https://example.test/infolegInternet/anexos/215000-219999/219405/texact.htm">bad</a>""",
            BaseUri);

        result.Candidates.Should().BeEmpty();
        result.LinksSkipped.Should().Be(1);
    }

    [Fact]
    public void InfolegLinkDiscovery_ShouldRejectJavascriptAndMailto()
    {
        var extractor = new InfolegLinkExtractor();

        var result = extractor.Extract(
            """
            <a href="javascript:alert(1)">bad</a>
            <a href="mailto:test@example.test">mail</a>
            """,
            BaseUri);

        result.Candidates.Should().BeEmpty();
        result.LinksSkipped.Should().Be(2);
    }

    [Fact]
    public void InfolegLinkDiscovery_ShouldRejectPathTraversal()
    {
        var extractor = new InfolegLinkExtractor();

        var result = extractor.Extract(
            """<a href="https://servicios.infoleg.gob.ar/infolegInternet/../secret.htm">bad</a>""",
            BaseUri);

        result.Candidates.Should().BeEmpty();
        result.LinksSkipped.Should().Be(1);
    }

    [Fact]
    public void InfolegLinkDiscovery_ShouldRejectUnexpectedQuerystrings()
    {
        var extractor = new InfolegLinkExtractor();

        var result = extractor.Extract(
            """<a href="https://servicios.infoleg.gob.ar/infolegInternet/anexos/215000-219999/219405/texact.htm?redirect=https://example.test">bad</a>""",
            BaseUri);

        result.Candidates.Should().BeEmpty();
        result.LinksSkipped.Should().Be(1);
    }

    [Fact]
    public void InfolegLinkDiscovery_ShouldRemoveFragments()
    {
        var extractor = new InfolegLinkExtractor();

        var result = extractor.Extract(
            """<a href="http://servicios.infoleg.gob.ar/infolegInternet/anexos/215000-219999/219405/texact.htm#art1">texact</a>""",
            BaseUri);

        result.Candidates.Should().ContainSingle()
            .Which.Url.Should().Be("https://servicios.infoleg.gob.ar/infolegInternet/anexos/215000-219999/219405/texact.htm");
    }

    [Fact]
    public void InfolegLinkDiscovery_ShouldDeduplicateUrls()
    {
        var extractor = new InfolegLinkExtractor();

        var result = extractor.Extract(
            """
            <a href="http://servicios.infoleg.gob.ar/infolegInternet/anexos/215000-219999/219405/texact.htm">a</a>
            <a href="https://servicios.infoleg.gob.ar/infolegInternet/anexos/215000-219999/219405/texact.htm#x">b</a>
            """,
            BaseUri);

        result.Candidates.Should().ContainSingle();
    }

    [Fact]
    public void InfolegLinkDiscovery_ShouldClassifyTexactLinks()
    {
        InfolegLinkExtractor
            .Classify("https://servicios.infoleg.gob.ar/infolegInternet/anexos/215000-219999/219405/texact.htm")
            .Should().Be(InfolegLinkClassification.Texact);
    }

    [Fact]
    public void InfolegLinkDiscovery_ShouldClassifyNormaLinks()
    {
        InfolegLinkExtractor
            .Classify("https://servicios.infoleg.gob.ar/infolegInternet/anexos/215000-219999/219405/norma.htm")
            .Should().Be(InfolegLinkClassification.Norma);
    }

    [Fact]
    public async Task DiscoverInfolegLinksCommand_ShouldWriteManifest()
    {
        using var testDirectory = TempDirectory.Create();
        await WriteInfolegSourceAsync(
            testDirectory.Path,
            "infoleg-rg-622-2013-norma.html",
            """
            <a href="https://servicios.infoleg.gob.ar/infolegInternet/anexos/215000-219999/219405/texact.htm">texact</a>
            <a href="https://example.test/external.htm">external</a>
            """);
        var manifestPath = Path.Combine(testDirectory.Path, "source-manifest", "infoleg.discovered.manifest.json");
        var service = CreateService();

        var response = await service.DiscoverAsync(
            new InfolegLinkDiscoveryRequest
            {
                SourceDirectory = testDirectory.Path,
                OutputManifestPath = manifestPath,
                MaxLinksPerSource = 20
            },
            CancellationToken.None);

        response.InfolegFilesInspected.Should().Be(1);
        response.LinksDiscovered.Should().Be(2);
        response.LinksAccepted.Should().Be(1);
        response.LinksSkipped.Should().Be(1);
        File.Exists(manifestPath).Should().BeTrue();

        var manifest = JsonSerializer.Deserialize<SourceManifest>(
            await File.ReadAllTextAsync(manifestPath),
            JsonOptions());
        manifest.Should().NotBeNull();
        manifest!.Source.Should().Be("InfolegLinkDiscovery");
        var source = manifest.Sources.Should().ContainSingle().Which;
        source.Id.Should().Be("infoleg-discovered-219405-texact");
        source.Source.Should().Be("Infoleg");
        source.Status.Should().Be("candidate");
        source.RequiresReview.Should().BeTrue();
        source.Priority.Should().Be("medium");
        source.Url.Should().Be("https://servicios.infoleg.gob.ar/infolegInternet/anexos/215000-219999/219405/texact.htm");
    }

    [Fact]
    public async Task DownloadSources_ShouldDownloadDiscoveredManifest()
    {
        using var testDirectory = TempDirectory.Create();
        var manifestPath = Path.Combine(testDirectory.Path, "infoleg.discovered.manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(
                new SourceManifest
                {
                    GeneratedAt = new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.Zero),
                    Source = "InfolegLinkDiscovery",
                    Sources =
                    [
                        new SourceManifestItem
                        {
                            Id = "infoleg-discovered-219405-texact",
                            Source = "Infoleg",
                            DocumentType = "Resolucion General",
                            ResolutionNumber = "622/2013",
                            Title = "Discovered Infoleg source",
                            Url = "https://servicios.infoleg.gob.ar/infolegInternet/anexos/215000-219999/219405/texact.htm",
                            FileName = "infoleg-discovered-219405-texact.html",
                            MetadataFileName = "infoleg-discovered-219405-texact.metadata.json",
                            Status = "candidate",
                            Priority = "medium",
                            RequiresReview = true
                        }
                    ]
                },
                JsonOptions()));
        var outputDirectory = Path.Combine(testDirectory.Path, "sources");
        var service = new ManifestSourceDownloadService(
            new HttpClient(new StaticResponseHandler("<html>downloaded</html>")),
            new FixedTimeProvider(new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.Zero)));

        var response = await service.DownloadAsync(
            new DownloadSourcesRequest
            {
                ManifestPath = manifestPath,
                OutputDirectory = outputDirectory
            },
            CancellationToken.None);

        response.SourcesDownloaded.Should().Be(1);
        File.Exists(Path.Combine(outputDirectory, "infoleg-discovered-219405-texact.html")).Should().BeTrue();
        File.Exists(Path.Combine(outputDirectory, "infoleg-discovered-219405-texact.metadata.json")).Should().BeTrue();
    }

    private static InfolegLinkDiscoveryService CreateService() =>
        new(
            new InfolegLinkExtractor(),
            new FixedTimeProvider(new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.Zero)));

    private static async Task WriteInfolegSourceAsync(string directory, string fileName, string html)
    {
        await File.WriteAllTextAsync(Path.Combine(directory, fileName), html);
        await File.WriteAllTextAsync(
            Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(fileName)}.metadata.json"),
            """
            {
              "id": "infoleg-rg-622-2013-norma",
              "source": "Infoleg",
              "documentType": "Resolucion General",
              "resolutionNumber": "622/2013",
              "title": "Resolucion General 622/2013 - Texto completo",
              "publicationDate": "2013-09-09",
              "url": "https://servicios.infoleg.gob.ar/infolegInternet/anexos/215000-219999/219405/norma.htm",
              "status": "candidate",
              "requiresReview": true
            }
            """);
    }

    private static JsonSerializerOptions JsonOptions() =>
        new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

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
