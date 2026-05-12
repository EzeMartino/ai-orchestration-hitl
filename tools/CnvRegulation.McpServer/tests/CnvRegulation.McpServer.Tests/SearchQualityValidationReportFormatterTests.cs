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
}
