using CnvRegulation.Application.Contracts;
using FluentAssertions;

namespace CnvRegulation.McpServer.Tests;

public sealed class ChunkQualityReportFormatterTests
{
    [Fact]
    public void InspectChunksCommand_ShouldReturnQualitySummary()
    {
        var report = new ChunkQualityReport
        {
            DocumentsAnalyzed = 1,
            ChunksAnalyzed = 2,
            VeryShortChunks = 1,
            VeryLongChunks = 1,
            Warnings =
            [
                new ChunkQualityWarning
                {
                    DocumentId = "cnv-toc-2013",
                    ChunkId = "chunk-1",
                    Article = "Artículo 1",
                    WarningType = "very_short",
                    Message = "very short chunk, 20 chars"
                }
            ]
        };

        var output = ChunkQualityReportFormatter.Format(report);

        output.Should().Contain("Documents analyzed: 1");
        output.Should().Contain("Chunks analyzed: 2");
        output.Should().Contain("Quality summary:");
        output.Should().Contain("- Very short chunks: 1");
        output.Should().Contain("[cnv-toc-2013] Artículo 1 - very short chunk, 20 chars");
    }
}
