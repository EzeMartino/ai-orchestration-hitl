namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Top search result details for relevance review.
/// </summary>
public sealed class SearchQualityReviewTopResult
{
    /// <summary>
    /// Gets source.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets article.
    /// </summary>
    public string? Article { get; init; }

    /// <summary>
    /// Gets score.
    /// </summary>
    public double? Score { get; init; }

    /// <summary>
    /// Gets score breakdown.
    /// </summary>
    public IReadOnlyDictionary<string, double>? ScoreBreakdown { get; init; }

    /// <summary>
    /// Gets snippet.
    /// </summary>
    public string? Snippet { get; init; }

    /// <summary>
    /// Gets citation.
    /// </summary>
    public SearchQualityReviewCitation? Citation { get; init; }
}
