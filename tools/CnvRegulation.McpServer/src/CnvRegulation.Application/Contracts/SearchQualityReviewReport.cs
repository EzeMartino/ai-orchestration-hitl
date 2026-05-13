namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Review-ready report for comparing full-text and hybrid search results.
/// </summary>
public sealed class SearchQualityReviewReport
{
    /// <summary>
    /// Gets report generation timestamp.
    /// </summary>
    public DateTimeOffset GeneratedAt { get; init; }

    /// <summary>
    /// Gets report summary.
    /// </summary>
    public required SearchQualityReviewSummary Summary { get; init; }

    /// <summary>
    /// Gets per-query review items.
    /// </summary>
    public required IReadOnlyList<SearchQualityReviewItem> Items { get; init; }
}
