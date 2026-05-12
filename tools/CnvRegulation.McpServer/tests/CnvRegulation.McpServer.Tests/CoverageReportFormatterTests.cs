using CnvRegulation.Application.Contracts;
using FluentAssertions;

namespace CnvRegulation.McpServer.Tests;

public sealed class CoverageReportFormatterTests
{
    [Fact]
    public void Format_ShouldReturnCoverageSummary()
    {
        var report = new RegulationCoverageReport
        {
            DocumentsTotal = 2,
            SourceDistribution =
            [
                new CoverageDistributionItem { Label = "Infoleg", Count = 2 }
            ],
            ResolutionNumberDistribution =
            [
                new CoverageDistributionItem { Label = "622/2013", Count = 1 },
                new CoverageDistributionItem { Label = "unknown", Count = 1 }
            ],
            ChunksTotal = 5,
            ChunksWithArticle = 4,
            ChunksWithoutArticle = 1,
            DistinctArticleCount = 4,
            DuplicateUrlCount = 1,
            DuplicateChunkCount = 2,
            VeryShortChunks = 0,
            VeryLongChunks = 0,
            DocumentsWithZeroChunks = 1,
            PotentialWrapperDocuments = 1,
            Warnings = ["1 documents produced zero chunks"]
        };

        var output = CoverageReportFormatter.Format(report);

        output.Should().Contain("Coverage report");
        output.Should().Contain("- Infoleg: 2");
        output.Should().Contain("- Duplicate URLs: 1");
        output.Should().Contain("- 1 documents produced zero chunks");
    }
}
