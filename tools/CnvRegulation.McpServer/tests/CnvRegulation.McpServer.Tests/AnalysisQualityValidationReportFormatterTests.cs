using CnvRegulation.Application.Contracts;
using FluentAssertions;

namespace CnvRegulation.McpServer.Tests;

public sealed class AnalysisQualityValidationReportFormatterTests
{
    [Fact]
    public void Format_ShouldShowPassFailTopicsFindingsAndCitations()
    {
        var report = CreateReport();

        var output = AnalysisQualityValidationReportFormatter.Format(report);

        output.Should().Contain("Analysis quality validation");
        output.Should().Contain("Cases: 2");
        output.Should().Contain("[PASS] alyc");
        output.Should().Contain("[FAIL] irrelevant");
        output.Should().Contain("Input: ALyC debe informar riesgos.");
        output.Should().Contain("Regulation area: ALyC");
        output.Should().Contain("Detected topics: ALyC obligaciones, Idoneidad");
        output.Should().Contain("Findings: 2");
        output.Should().Contain("Citations: 2");
        output.Should().Contain("Reason: expected status insufficient_evidence, got requires_review");
    }

    [Fact]
    public async Task AnalysisQualityValidationReportWriter_ShouldWriteJsonReport()
    {
        using var testDirectory = TempDirectory.Create();
        var reportPath = Path.Combine(testDirectory.Path, "analysis-quality.json");

        var writtenPath = await AnalysisQualityValidationReportWriter.WriteAsync(
            CreateReport(),
            reportPath,
            CancellationToken.None);

        writtenPath.Should().Be(Path.GetFullPath(reportPath));
        var json = await File.ReadAllTextAsync(reportPath);
        json.Should().Contain("\"caseSetPath\": \"cases.json\"");
        json.Should().Contain("\"useHybridSearch\": true");
        json.Should().Contain("\"detectedTopics\"");
    }

    [Fact]
    public async Task AnalysisQualityValidationReportWriter_ShouldWriteMarkdownReport()
    {
        using var testDirectory = TempDirectory.Create();
        var reportPath = Path.Combine(testDirectory.Path, "analysis-quality.md");

        await AnalysisQualityValidationReportWriter.WriteAsync(
            CreateReport(),
            reportPath,
            CancellationToken.None);

        var markdown = await File.ReadAllTextAsync(reportPath);
        markdown.Should().Contain("# Analysis Quality Validation");
        markdown.Should().Contain("## PASS alyc");
        markdown.Should().Contain("- Input: ALyC debe informar riesgos.");
        markdown.Should().Contain("- Hybrid search: enabled");
    }

    private static AnalysisQualityValidationReport CreateReport() =>
        new()
        {
            CaseSetPath = "cases.json",
            UseHybridSearch = true,
            Cases = 2,
            Passed = 1,
            Failed = 1,
            Warnings = [],
            Results =
            [
                new AnalysisQualityValidationResult
                {
                    Id = "alyc",
                    Text = "ALyC debe informar riesgos.",
                    RegulationArea = "ALyC",
                    ExpectedStatus = "requires_review",
                    ActualStatus = "requires_review",
                    ExpectedTopics = ["ALyC obligaciones"],
                    DetectedTopics = ["ALyC obligaciones", "Idoneidad"],
                    FindingsCount = 2,
                    CitationsCount = 2,
                    RiskLevels = ["medium"],
                    Warnings = ["candidate source"],
                    Passed = true,
                    FailureReasons = []
                },
                new AnalysisQualityValidationResult
                {
                    Id = "irrelevant",
                    Text = "Cambio de color.",
                    RegulationArea = null,
                    ExpectedStatus = "insufficient_evidence",
                    ActualStatus = "requires_review",
                    ExpectedTopics = [],
                    DetectedTopics = [],
                    FindingsCount = 1,
                    CitationsCount = 1,
                    RiskLevels = ["low"],
                    Warnings = [],
                    Passed = false,
                    FailureReasons = ["expected status insufficient_evidence, got requires_review"]
                }
            ]
        };

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
