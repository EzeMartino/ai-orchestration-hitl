using CnvRegulation.Application.Contracts;
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
            Warnings = []
        };

        var output = EmbeddingGenerationReportFormatter.Format(response);

        output.Should().Contain("Embedding generation");
        output.Should().Contain("Generated: 6");
        output.Should().Contain("Eligible chunks: 6");
        output.Should().Contain("Actual tokens: n/a");
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
}
