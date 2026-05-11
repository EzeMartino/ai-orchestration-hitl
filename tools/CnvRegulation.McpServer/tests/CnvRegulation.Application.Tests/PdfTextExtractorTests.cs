using CnvRegulation.Infrastructure.Parsing;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class PdfTextExtractorTests
{
    [Fact]
    public async Task PdfTextExtractor_ShouldExtractTextFromSamplePdf()
    {
        using var testDirectory = TempDirectory.Create();
        var pdfPath = Path.Combine(testDirectory.Path, "sample-regulation.pdf");
        await PdfTestDocumentFactory.WriteSampleRegulationPdfAsync(pdfPath);
        var extractor = new PdfPigTextExtractor();

        var result = await extractor.ExtractAsync(pdfPath, CancellationToken.None);

        result.Text.Should().Contain("ARTICULO 1");
        result.Text.Should().Contain("Texto de prueba PDF");
        result.Pages.Should().ContainSingle();
        result.Warnings.Should().BeEmpty();
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
