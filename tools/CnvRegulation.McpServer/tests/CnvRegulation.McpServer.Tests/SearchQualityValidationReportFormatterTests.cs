using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;
using FluentAssertions;

namespace CnvRegulation.McpServer.Tests;

public sealed class SearchQualityValidationReportFormatterTests
{
    [Fact]
    public void Format_ShouldShowPassFailAndExpandedQueries()
    {
        var report = new SearchQualityValidationReport
        {
            QuerySetPath = "queries.json",
            Queries = 2,
            Passed = 1,
            Failed = 1,
            Warnings = [],
            Results =
            [
                new SearchQualityValidationResult
                {
                    Id = "alyc",
                    Query = "ALyC obligaciones",
                    ExpandedTerms = ["agente de liquidacion y compensacion"],
                    ExpandedQueries = ["ALyC obligaciones", "agente de liquidacion y compensacion obligaciones"],
                    ResultCount = 3,
                    TopResultSource = "CNV",
                    TopResultTitle = "Normas CNV",
                    TopResultArticle = "Articulo 1",
                    TopResultScore = 0.91,
                    CitationsPresent = true,
                    Warnings = ["candidate source"],
                    Passed = true,
                    FailureReasons = []
                },
                new SearchQualityValidationResult
                {
                    Id = "lavado",
                    Query = "prevencion de lavado",
                    ExpandedTerms = [],
                    ExpandedQueries = ["prevencion de lavado"],
                    ResultCount = 0,
                    TopResultSource = null,
                    TopResultTitle = null,
                    TopResultArticle = null,
                    TopResultScore = null,
                    CitationsPresent = false,
                    Warnings = [],
                    Passed = false,
                    FailureReasons = ["expected at least 1 result"]
                }
            ]
        };

        var output = SearchQualityValidationReportFormatter.Format(report);

        output.Should().Contain("Search quality validation");
        output.Should().Contain("[PASS] alyc");
        output.Should().Contain("[FAIL] lavado");
        output.Should().Contain("Expanded: ALyC obligaciones, agente de liquidacion y compensacion obligaciones");
        output.Should().Contain("Citations: yes");
        output.Should().Contain("Reason: expected at least 1 result");
    }

    [Fact]
    public void ExplainSearchQueryFormat_ShouldShowDiagnostics()
    {
        var report = new ExplainSearchQueryReport
        {
            OriginalQuery = "hecho relevante",
            NormalizedQuery = "hecho relevante",
            ExpandedTerms = ["informacion relevante"],
            GeneratedQueries =
            [
                new GeneratedSearchQueryDiagnostic
                {
                    Query = "informacion relevante",
                    RawResultCount = 2
                }
            ],
            TermPresence =
            [
                new SearchTermPresence
                {
                    Term = "informacion relevante",
                    FoundInChunks = 2,
                    FoundInDocuments = 1,
                    ExactPhraseFound = true
                }
            ],
            TopPartialMatches =
            [
                new SearchPartialMatch
                {
                    DocumentId = "cnv",
                    ChunkId = "cnv-art-1",
                    Source = "CNV",
                    Title = "Normas CNV",
                    Article = "Articulo 1",
                    Score = 1,
                    Snippet = "Texto relevante"
                }
            ],
            HiddenDuplicateResults = 1,
            HiddenNonSearchableResults = 0,
            FiltersApplied = ["source=CNV"],
            Warnings = ["candidate source"],
            Recommendation = "Alias expansion is recovering results; inspect expanded-query matches."
        };

        var output = ExplainSearchQueryReportFormatter.Format(report);

        output.Should().Contain("Query explanation");
        output.Should().Contain("Normalized query: hecho relevante");
        output.Should().Contain("Raw results: 2");
        output.Should().Contain("Hidden duplicates: 1");
        output.Should().Contain("Recommendation:");
    }

    [Fact]
    public void EmbeddingGenerationFormat_ShouldShowSummary()
    {
        var response = new GenerateEmbeddingsResponse
        {
            ChunksScanned = 10,
            MissingEmbeddings = 8,
            EligibleChunks = 6,
            AlreadyEmbedded = 2,
            Generated = 6,
            Failed = 0,
            SkippedDuplicates = 1,
            SkippedNonSearchable = 1,
            SkippedEmpty = 0,
            EstimatedTokenCount = 120,
            ActualTokenCount = null,
            Provider = "Fake",
            Model = "fake-deterministic",
            Dimensions = 1536,
            Warnings = [],
            FailedReportPath = "data/embedding-reports/failed-embeddings.json",
            FailedItems =
            [
                new EmbeddingFailureItem
                {
                    DocumentId = "doc",
                    ChunkId = "chunk-long",
                    Source = "CNV",
                    Title = "Normas CNV",
                    Article = "Articulo 1",
                    Status = "skipped_too_long",
                    Reason = "Chunk length exceeds max input length.",
                    TextLength = 64000,
                    EstimatedTokens = 16000
                }
            ]
        };

        var output = EmbeddingGenerationReportFormatter.Format(response);

        output.Should().Contain("Embedding generation");
        output.Should().Contain("Generated: 6");
        output.Should().Contain("Eligible chunks: 6");
        output.Should().Contain("Actual tokens: n/a");
        output.Should().Contain("Failed report: data/embedding-reports/failed-embeddings.json");
        output.Should().Contain("Failure: chunk-long | skipped_too_long");
        output.Should().Contain("Provider: Fake");
        output.Should().Contain("Dimensions: 1536");
    }

    [Fact]
    public void SearchQualityComparisonFormat_ShouldShowModeDifferencesAndScoreBreakdown()
    {
        var report = new SearchQualityComparisonReport
        {
            QuerySetPath = "queries.json",
            BaselineMode = "full_text",
            CandidateMode = "hybrid",
            Queries = 1,
            BaselinePassed = 1,
            BaselineFailed = 0,
            CandidatePassed = 1,
            CandidateFailed = 0,
            Warnings = [],
            Results =
            [
                new SearchQualityComparisonResult
                {
                    Id = "alyc",
                    Query = "ALyC obligaciones",
                    BaselinePassed = true,
                    CandidatePassed = true,
                    BaselineTopResult = "CNV | Normas CNV | Articulo 1 | 0.800",
                    CandidateTopResult = "CNV | Normas CNV | Articulo 2 | 0.900",
                    TopResultChanged = true,
                    CandidateScoreBreakdown = "fullTextScore=0.8;vectorScore=0.7;finalScore=0.76",
                    FailureReasons = []
                }
            ]
        };

        var output = SearchQualityComparisonReportFormatter.Format(report);

        output.Should().Contain("Search quality comparison");
        output.Should().Contain("full_text: 1 passed, 0 failed");
        output.Should().Contain("hybrid top:");
        output.Should().Contain("Top changed: yes");
        output.Should().Contain("Score breakdown: fullTextScore=0.8;vectorScore=0.7;finalScore=0.76");
    }

    [Fact]
    public async Task SearchQualityReviewReportWriter_ShouldWriteJsonReport()
    {
        using var testDirectory = TempDirectory.Create();
        var reportPath = Path.Combine(testDirectory.Path, "fulltext-vs-hybrid.review.json");
        var comparison = CreateComparisonReport();
        var report = SearchQualityReviewReportFactory.Create(comparison, DateTimeOffset.Parse("2026-05-13T00:00:00Z"));

        var writtenPath = await SearchQualityReviewReportWriter.WriteAsync(report, reportPath, CancellationToken.None);

        writtenPath.Should().Be(Path.GetFullPath(reportPath));
        var json = await File.ReadAllTextAsync(reportPath);
        json.Should().Contain("\"generatedAt\": \"2026-05-13T00:00:00+00:00\"");
        json.Should().Contain("\"reviewDecision\": \"unknown\"");
        json.Should().Contain("\"scoreBreakdown\"");
        json.Should().Contain("\"citation\"");
    }

    [Fact]
    public async Task SearchQualityReviewReportWriter_ShouldWriteMarkdownReport()
    {
        using var testDirectory = TempDirectory.Create();
        var reportPath = Path.Combine(testDirectory.Path, "fulltext-vs-hybrid.review.md");
        var comparison = CreateComparisonReport();
        var report = SearchQualityReviewReportFactory.Create(comparison, DateTimeOffset.Parse("2026-05-13T00:00:00Z"));

        await SearchQualityReviewReportWriter.WriteAsync(report, reportPath, CancellationToken.None);

        var markdown = await File.ReadAllTextAsync(reportPath);
        markdown.Should().Contain("# Search Quality Review");
        markdown.Should().Contain("## alyc");
        markdown.Should().Contain("### Full-text");
        markdown.Should().Contain("### Hybrid");
        markdown.Should().Contain("Review decision: unknown");
    }

    private static SearchQualityComparisonReport CreateComparisonReport() =>
        new()
        {
            QuerySetPath = "queries.json",
            BaselineMode = "full_text",
            CandidateMode = "hybrid",
            Queries = 1,
            BaselinePassed = 1,
            BaselineFailed = 0,
            CandidatePassed = 1,
            CandidateFailed = 0,
            Warnings = [],
            Results =
            [
                new SearchQualityComparisonResult
                {
                    Id = "alyc",
                    Query = "ALyC obligaciones",
                    BaselinePassed = true,
                    CandidatePassed = true,
                    BaselineTopResult = "CNV | Normas CNV | Articulo 1 | 0.800",
                    BaselineResult = CreateValidationResult(
                        "ALyC obligaciones",
                        "CNV",
                        "Normas CNV",
                        "Articulo 1",
                        0.8,
                        scoreBreakdown: null),
                    CandidateTopResult = "Infoleg | RG 622 | Articulo 4 | 0.900",
                    CandidateResult = CreateValidationResult(
                        "ALyC obligaciones",
                        "Infoleg",
                        "RG 622",
                        "Articulo 4",
                        0.9,
                        "fullTextScore=0.8;vectorScore=0.7;sourcePriorityBonus=0.1;multiQueryBonus=0.03;finalScore=0.76"),
                    TopResultChanged = true,
                    CandidateScoreBreakdown = "fullTextScore=0.8;vectorScore=0.7;finalScore=0.76",
                    FailureReasons = []
                }
            ]
        };

    private static SearchQualityValidationResult CreateValidationResult(
        string query,
        string source,
        string title,
        string article,
        double score,
        string? scoreBreakdown) =>
        new()
        {
            Id = "alyc",
            Query = query,
            ExpandedTerms = ["agente de liquidacion y compensacion"],
            ExpandedQueries = ["alyc obligaciones", "agente de liquidacion y compensacion obligaciones"],
            ResultCount = 1,
            TopResultSource = source,
            TopResultTitle = title,
            TopResultArticle = article,
            TopResultScore = score,
            TopResultSnippet = "snippet regulatorio",
            TopResultCitation = new RegulationCitation
            {
                Source = source,
                DocumentType = "Texto Ordenado",
                Title = title,
                Article = article,
                Url = "https://www.cnv.gov.ar/",
                QuotedText = "snippet regulatorio"
            },
            TopResultMetadata = string.IsNullOrWhiteSpace(scoreBreakdown)
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["scoreBreakdown"] = scoreBreakdown
                },
            CitationsPresent = true,
            Warnings = ["candidate source"],
            Passed = true,
            FailureReasons = []
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
