namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Citation summary for search-quality review output.
/// </summary>
public sealed class SearchQualityReviewCitation
{
    /// <summary>
    /// Gets citation source.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets citation title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets citation article.
    /// </summary>
    public string? Article { get; init; }

    /// <summary>
    /// Gets citation URL.
    /// </summary>
    public required string Url { get; init; }
}
