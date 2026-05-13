using System.Globalization;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;

namespace CnvRegulation.McpServer;

/// <summary>
/// Builds review-ready search-quality reports.
/// </summary>
public static class SearchQualityReviewReportFactory
{
    /// <summary>
    /// Creates a review report from a comparison report.
    /// </summary>
    /// <param name="comparison">The comparison report.</param>
    /// <param name="generatedAt">The generation timestamp.</param>
    /// <returns>The review report.</returns>
    public static SearchQualityReviewReport Create(
        SearchQualityComparisonReport comparison,
        DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        var items = comparison.Results
            .Select(CreateItem)
            .ToArray();

        return new SearchQualityReviewReport
        {
            GeneratedAt = generatedAt,
            Summary = new SearchQualityReviewSummary
            {
                Queries = comparison.Queries,
                FullTextPassed = comparison.BaselinePassed,
                HybridPassed = comparison.CandidatePassed,
                TopResultChanged = items.Count(item => item.TopResultChanged)
            },
            Items = items
        };
    }

    private static SearchQualityReviewItem CreateItem(SearchQualityComparisonResult result)
    {
        var baseline = result.BaselineResult;
        var candidate = result.CandidateResult;

        return new SearchQualityReviewItem
        {
            QueryId = result.Id,
            Query = result.Query,
            ExpandedQueries = baseline?.ExpandedQueries ?? candidate?.ExpandedQueries ?? [],
            FullText = CreateModeResult(baseline),
            Hybrid = CreateModeResult(candidate),
            TopResultChanged = result.TopResultChanged,
            ReviewDecision = "unknown",
            ReviewNotes = null
        };
    }

    private static SearchQualityReviewModeResult CreateModeResult(SearchQualityValidationResult? result) =>
        new()
        {
            Passed = result?.Passed ?? false,
            TopResult = CreateTopResult(result),
            Warnings = result?.Warnings ?? []
        };

    private static SearchQualityReviewTopResult? CreateTopResult(SearchQualityValidationResult? result)
    {
        if (result is null || string.IsNullOrWhiteSpace(result.TopResultSource))
        {
            return null;
        }

        return new SearchQualityReviewTopResult
        {
            Source = result.TopResultSource,
            Title = result.TopResultTitle ?? string.Empty,
            Article = result.TopResultArticle,
            Score = result.TopResultScore,
            ScoreBreakdown = ParseScoreBreakdown(result.TopResultMetadata.GetValueOrDefault("scoreBreakdown")),
            Snippet = result.TopResultSnippet,
            Citation = CreateCitation(result.TopResultCitation)
        };
    }

    private static SearchQualityReviewCitation? CreateCitation(RegulationCitation? citation)
    {
        if (citation is null)
        {
            return null;
        }

        return new SearchQualityReviewCitation
        {
            Source = citation.Source,
            Title = citation.Title,
            Article = citation.Article,
            Url = citation.Url
        };
    }

    private static IReadOnlyDictionary<string, double>? ParseScoreBreakdown(string? scoreBreakdown)
    {
        if (string.IsNullOrWhiteSpace(scoreBreakdown))
        {
            return null;
        }

        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in scoreBreakdown.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', count: 2, StringSplitOptions.TrimEntries);
            if (pair.Length == 2 && double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                values[pair[0]] = value;
            }
        }

        return values.Count == 0 ? null : values;
    }
}
