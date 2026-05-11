using CnvRegulation.Application.Contracts;
using CnvRegulation.Infrastructure.Chunking;
using CnvRegulation.Infrastructure.Diagnostics;
using CnvRegulation.Infrastructure.Ingestion;
using CnvRegulation.Infrastructure.Parsing;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class SourceInspectionServiceTests
{
    [Fact]
    public async Task InspectAsync_ShouldReportChunksAndUnsupportedFiles()
    {
        using var testDirectory = TempDirectory.Create();
        await WriteSourceAsync(
            testDirectory.Path,
            "infoleg-rg-622-2013-texact.html",
            """
            <html><body>
            <h1>Resolucion General</h1>
            <p>TÍTULO I</p>
            <p>CAPÍTULO I</p>
            <p>ARTÍCULO 1°.- Este es el primer artículo realista.</p>
            <p>ARTÍCULO 2°.- Este es el segundo artículo realista.</p>
            </body></html>
            """,
            CreateMetadataJson(
                "infoleg-rg-622-2013-texact",
                "Infoleg",
                "Resolución General",
                "Resolución General 622/2013 - Texto actualizado",
                "candidate",
                requiresReview: true));
        await WriteSourceAsync(
            testDirectory.Path,
            "cnv-marco-regulatorio.html",
            "<html><body><h1>Marco Regulatorio</h1><p>Contenido sin articulos.</p></body></html>",
            CreateMetadataJson(
                "cnv-marco-regulatorio",
                "CNV",
                "Marco Regulatorio",
                "CNV Marco Regulatorio",
                "candidate",
                requiresReview: true));
        await PdfTestDocumentFactory.WriteSampleRegulationPdfAsync(Path.Combine(testDirectory.Path, "cnv-toc-2013.pdf"));
        await File.WriteAllTextAsync(
            Path.Combine(testDirectory.Path, "cnv-toc-2013.metadata.json"),
            CreateMetadataJson(
                "cnv-toc-2013",
                "CNV",
                "Texto Ordenado",
                "Normas CNV N.T. 2013",
                "candidate",
                requiresReview: true));
        var service = CreateService();

        var response = await service.InspectAsync(
            new InspectSourcesRequest { SourceDirectory = testDirectory.Path },
            CancellationToken.None);

        response.DocumentsInspected.Should().Be(3);
        response.DocumentsWithChunks.Should().Be(2);
        response.DocumentsWithoutChunks.Should().Be(1);
        response.UnsupportedFiles.Should().Be(0);

        var infoleg = response.Documents.Single(document => document.FileName == "infoleg-rg-622-2013-texact.html");
        infoleg.Source.Should().Be("Infoleg");
        infoleg.Title.Should().Be("Resolución General 622/2013 - Texto actualizado");
        infoleg.DocumentType.Should().Be("Resolución General");
        infoleg.ExtractedTextLength.Should().BeGreaterThan(0);
        infoleg.ChunkCount.Should().Be(2);
        infoleg.DetectedTitles.Should().Contain("Título I");
        infoleg.DetectedChapters.Should().Contain("Capítulo I");
        infoleg.DetectedArticles.Should().ContainInOrder("Artículo 1", "Artículo 2");
        infoleg.FirstArticles.Should().ContainInOrder("Artículo 1", "Artículo 2");
        infoleg.Warnings.Should().Contain(["candidate source", "requires review"]);

        var marco = response.Documents.Single(document => document.FileName == "cnv-marco-regulatorio.html");
        marco.ChunkCount.Should().Be(0);
        marco.Warnings.Should().Contain("no article boundaries detected");

        var pdf = response.Documents.Single(document => document.FileName == "cnv-toc-2013.pdf");
        pdf.IsUnsupported.Should().BeFalse();
        pdf.FileType.Should().Be("PDF");
        pdf.PageCount.Should().Be(1);
        pdf.ExtractedTextLength.Should().BeGreaterThan(0);
        pdf.ChunkCount.Should().Be(2);
        pdf.DetectedArticles.Should().ContainInOrder("Artículo 1", "Artículo 2");
        pdf.Warnings.Should().Contain(["candidate source", "requires review"]);
    }

    [Fact]
    public async Task InspectSources_ShouldReportPdfPagesAndExtractedTextLength()
    {
        using var testDirectory = TempDirectory.Create();
        await PdfTestDocumentFactory.WriteSampleRegulationPdfAsync(Path.Combine(testDirectory.Path, "sample.pdf"));
        await File.WriteAllTextAsync(
            Path.Combine(testDirectory.Path, "sample.metadata.json"),
            CreateMetadataJson(
                "sample-pdf",
                "CNV",
                "Texto Ordenado",
                "Sample PDF",
                "candidate",
                requiresReview: true));
        var service = CreateService();

        var response = await service.InspectAsync(
            new InspectSourcesRequest { SourceDirectory = testDirectory.Path },
            CancellationToken.None);

        var pdf = response.Documents.Should().ContainSingle().Which;
        pdf.FileType.Should().Be("PDF");
        pdf.PageCount.Should().Be(1);
        pdf.ExtractedTextLength.Should().BeGreaterThan(0);
        pdf.ChunkCount.Should().Be(2);
        pdf.FirstArticles.Should().ContainInOrder("Artículo 1", "Artículo 2");
    }

    private static SourceInspectionService CreateService() =>
        new(
            new PlainTextRegulationParser(),
            new HtmlRegulationParser(),
            new PdfPigTextExtractor(),
            new PdfExtractedTextNormalizer(),
            new SidecarMetadataReader(),
            new LegalStructureRegulationChunker(new LegalStructureDetector()));

    private static async Task WriteSourceAsync(
        string directory,
        string sourceFileName,
        string content,
        string metadataJson)
    {
        await File.WriteAllTextAsync(Path.Combine(directory, sourceFileName), content);
        await File.WriteAllTextAsync(
            Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(sourceFileName)}.metadata.json"),
            metadataJson);
    }

    private static string CreateMetadataJson(
        string id,
        string source,
        string documentType,
        string title,
        string status,
        bool requiresReview) =>
        $$"""
        {
          "id": "{{id}}",
          "source": "{{source}}",
          "documentType": "{{documentType}}",
          "resolutionNumber": "622/2013",
          "title": "{{title}}",
          "publicationDate": "2013-09-09",
          "effectiveDate": null,
          "url": "https://example.test/{{id}}",
          "status": "{{status}}",
          "requiresReview": {{requiresReview.ToString().ToLowerInvariant()}}
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
