using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Analysis;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.Diagnostics;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class AnalysisQualityValidationServiceTests
{
    [Fact]
    public async Task ValidateAnalysisQuality_ShouldPass_WhenExpectedStatusMatches()
    {
        using var testDirectory = TempDirectory.Create();
        var caseSetPath = await WriteCaseSetAsync(
            testDirectory.Path,
            """
            {
              "cases": [
                {
                  "id": "alyc",
                  "text": "ALyC debe informar riesgos a clientes.",
                  "regulationArea": "ALyC",
                  "expectedStatus": "requires_review",
                  "expectedTopics": [ "ALyC obligaciones" ],
                  "minFindings": 1,
                  "minCitations": 1
                }
              ]
            }
            """);
        var validator = new AnalysisQualityValidationService(
            new StaticAnalysisService("requires_review", findings: 1),
            new RegulatoryTopicExtractor());

        var report = await validator.ValidateAsync(
            new ValidateAnalysisQualityRequest { CaseSetPath = caseSetPath },
            CancellationToken.None);

        report.Cases.Should().Be(1);
        report.Passed.Should().Be(1);
        report.Failed.Should().Be(0);
        report.Results[0].ActualStatus.Should().Be("requires_review");
    }

    [Fact]
    public async Task ValidateAnalysisQuality_ShouldFail_WhenStatusDiffers()
    {
        using var testDirectory = TempDirectory.Create();
        var caseSetPath = await WriteCaseSetAsync(
            testDirectory.Path,
            """
            {
              "cases": [
                {
                  "id": "status",
                  "text": "ALyC debe informar riesgos a clientes.",
                  "expectedStatus": "requires_review",
                  "expectedTopics": [],
                  "minFindings": 0,
                  "minCitations": 0
                }
              ]
            }
            """);
        var validator = new AnalysisQualityValidationService(
            new StaticAnalysisService("insufficient_evidence", findings: 0),
            new RegulatoryTopicExtractor());

        var report = await validator.ValidateAsync(
            new ValidateAnalysisQualityRequest { CaseSetPath = caseSetPath },
            CancellationToken.None);

        report.Failed.Should().Be(1);
        report.Results[0].FailureReasons.Should().Contain(reason =>
            reason.Contains("expected status requires_review", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAnalysisQuality_ShouldCheckMinimumFindings()
    {
        using var testDirectory = TempDirectory.Create();
        var caseSetPath = await WriteCaseSetAsync(
            testDirectory.Path,
            """
            {
              "cases": [
                {
                  "id": "findings",
                  "text": "ALyC debe informar riesgos a clientes.",
                  "expectedStatus": "requires_review",
                  "expectedTopics": [],
                  "minFindings": 2,
                  "minCitations": 0
                }
              ]
            }
            """);
        var validator = new AnalysisQualityValidationService(
            new StaticAnalysisService("requires_review", findings: 1),
            new RegulatoryTopicExtractor());

        var report = await validator.ValidateAsync(
            new ValidateAnalysisQualityRequest { CaseSetPath = caseSetPath },
            CancellationToken.None);

        report.Results[0].FailureReasons.Should().Contain(reason =>
            reason.Contains("expected at least 2 finding", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAnalysisQuality_ShouldCheckMinimumCitations()
    {
        using var testDirectory = TempDirectory.Create();
        var caseSetPath = await WriteCaseSetAsync(
            testDirectory.Path,
            """
            {
              "cases": [
                {
                  "id": "citations",
                  "text": "ALyC debe informar riesgos a clientes.",
                  "expectedStatus": "requires_review",
                  "expectedTopics": [],
                  "minFindings": 1,
                  "minCitations": 2
                }
              ]
            }
            """);
        var validator = new AnalysisQualityValidationService(
            new StaticAnalysisService("requires_review", findings: 1),
            new RegulatoryTopicExtractor());

        var report = await validator.ValidateAsync(
            new ValidateAnalysisQualityRequest { CaseSetPath = caseSetPath },
            CancellationToken.None);

        report.Results[0].CitationsCount.Should().Be(1);
        report.Results[0].FailureReasons.Should().Contain(reason =>
            reason.Contains("expected at least 2 citation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAnalysisQuality_ShouldReportDetectedTopics()
    {
        using var testDirectory = TempDirectory.Create();
        var caseSetPath = await WriteCaseSetAsync(
            testDirectory.Path,
            """
            {
              "cases": [
                {
                  "id": "topics",
                  "text": "La emisora informara un hecho relevante por AIF.",
                  "regulationArea": "Emisoras",
                  "expectedStatus": "requires_review",
                  "expectedTopics": [ "hecho relevante", "emisora" ],
                  "minFindings": 1,
                  "minCitations": 1
                }
              ]
            }
            """);
        var validator = new AnalysisQualityValidationService(
            new StaticAnalysisService("requires_review", findings: 1),
            new RegulatoryTopicExtractor());

        var report = await validator.ValidateAsync(
            new ValidateAnalysisQualityRequest { CaseSetPath = caseSetPath },
            CancellationToken.None);

        report.Passed.Should().Be(1);
        report.Results[0].DetectedTopics.Should().Contain("Hecho relevante");
        report.Results[0].DetectedTopics.Should().Contain("Emisora");
    }

    [Fact]
    public async Task ValidateAnalysisQuality_ShouldSupportHybridFlag()
    {
        using var testDirectory = TempDirectory.Create();
        var caseSetPath = await WriteCaseSetAsync(
            testDirectory.Path,
            """
            {
              "cases": [
                {
                  "id": "hybrid",
                  "text": "ALyC debe informar riesgos a clientes.",
                  "expectedStatus": "requires_review",
                  "expectedTopics": [],
                  "minFindings": 1,
                  "minCitations": 1
                }
              ]
            }
            """);
        var analysisService = new CapturingAnalysisService();
        var validator = new AnalysisQualityValidationService(
            analysisService,
            new RegulatoryTopicExtractor());

        var report = await validator.ValidateAsync(
            new ValidateAnalysisQualityRequest
            {
                CaseSetPath = caseSetPath,
                UseHybridSearch = true
            },
            CancellationToken.None);

        report.UseHybridSearch.Should().BeTrue();
        analysisService.LastRequest!.UseHybridSearch.Should().BeTrue();
        report.Passed.Should().Be(1);
    }

    private static async Task<string> WriteCaseSetAsync(string directory, string json)
    {
        var path = Path.Combine(directory, "analysis-cases.json");
        await File.WriteAllTextAsync(path, json);

        return path;
    }

    private sealed class StaticAnalysisService(string status, int findings) : IComplianceAnalysisService
    {
        public Task<AnalyzeTextAgainstCnvResponse> AnalyzeAsync(
            AnalyzeTextAgainstCnvRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AnalyzeTextAgainstCnvResponse
            {
                Status = status,
                Findings = Enumerable.Range(1, findings).Select(CreateFinding).ToArray(),
                Warnings = ["candidate source"],
                Disclaimer = "This is an automated regulatory review aid, not legal advice."
            });
    }

    private sealed class CapturingAnalysisService : IComplianceAnalysisService
    {
        public AnalyzeTextAgainstCnvRequest? LastRequest { get; private set; }

        public Task<AnalyzeTextAgainstCnvResponse> AnalyzeAsync(
            AnalyzeTextAgainstCnvRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;

            return Task.FromResult(new AnalyzeTextAgainstCnvResponse
            {
                Status = "requires_review",
                Findings = [CreateFinding(1)],
                Warnings = ["candidate source"],
                Disclaimer = "This is an automated regulatory review aid, not legal advice."
            });
        }
    }

    private static ComplianceFinding CreateFinding(int index) =>
        new()
        {
            RiskLevel = "medium",
            Issue = "Issue",
            Citation = new RegulationCitation
            {
                Source = "CNV",
                DocumentType = "Texto Ordenado",
                Title = "Normas CNV",
                Article = $"Articulo {index}",
                Url = "https://www.cnv.gov.ar/",
                QuotedText = "Texto citado."
            },
            ReasoningSummary = "Human legal review is recommended.",
            Confidence = 0.7
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
