using CnvRegulation.Application.Contracts;
using FluentAssertions;

namespace CnvRegulation.McpServer.Tests;

public sealed class SourceInspectionReportFormatterTests
{
    [Fact]
    public void Format_ShouldRenderDiagnosticsSummaryAndDocumentDetails()
    {
        var response = new InspectSourcesResponse
        {
            DocumentsInspected = 2,
            DocumentsWithChunks = 1,
            DocumentsWithoutChunks = 1,
            UnsupportedFiles = 1,
            Warnings = [],
            Documents =
            [
                new SourceInspectionDocumentResult
                {
                    FileName = "infoleg-rg-622-2013-texact.html",
                    Source = "Infoleg",
                    Title = "Resolución General 622/2013 - Texto actualizado",
                    DocumentType = "Resolución General",
                    ExtractedTextLength = 154321,
                    ChunkCount = 420,
                    DetectedTitles = ["Título I"],
                    DetectedChapters = ["Capítulo I"],
                    DetectedSections = [],
                    DetectedArticles = ["Artículo 1", "Artículo 2", "Artículo 3"],
                    FirstArticles = ["Artículo 1", "Artículo 2", "Artículo 3"],
                    IsUnsupported = false,
                    Warnings = ["candidate source", "requires review"]
                }
            ]
        };

        var report = SourceInspectionReportFormatter.Format(response);

        report.Should().Contain("Documents inspected: 2");
        report.Should().Contain("Documents with chunks: 1");
        report.Should().Contain("Unsupported files: 1");
        report.Should().Contain("[infoleg-rg-622-2013-texact.html]");
        report.Should().Contain("Source: Infoleg");
        report.Should().Contain("Chunks: 420");
        report.Should().Contain("Articles detected: 3");
        report.Should().Contain("First articles: Artículo 1, Artículo 2, Artículo 3");
        report.Should().Contain("Warnings: candidate source, requires review");
    }
}
