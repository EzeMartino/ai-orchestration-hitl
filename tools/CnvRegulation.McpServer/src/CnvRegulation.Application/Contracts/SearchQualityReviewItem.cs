namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Per-query item for manual relevance review.
/// </summary>
public sealed class SearchQualityReviewItem
{
    /// <summary>
    /// Gets query identifier.
    /// </summary>
    public required string QueryId { get; init; }

    /// <summary>
    /// Gets original query.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Gets expanded queries.
    /// </summary>
    public required IReadOnlyList<string> ExpandedQueries { get; init; }

    /// <summary>
    /// Gets full-text result.
    /// </summary>
    public required SearchQualityReviewModeResult FullText { get; init; }

    /// <summary>
    /// Gets hybrid result.
    /// </summary>
    public required SearchQualityReviewModeResult Hybrid { get; init; }

    /// <summary>
    /// Gets whether top result changed.
    /// </summary>
    public bool TopResultChanged { get; init; }

    /// <summary>
    /// Gets manual review decision.
    /// </summary>
    public string ReviewDecision { get; init; } = "unknown";

    /// <summary>
    /// Gets manual review notes.
    /// </summary>
    public string? ReviewNotes { get; init; }
}
