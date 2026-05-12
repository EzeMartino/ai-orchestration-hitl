namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Request for search-quality validation.
/// </summary>
public sealed class ValidateSearchQualityRequest
{
    /// <summary>
    /// Gets the query set JSON path.
    /// </summary>
    public required string QuerySetPath { get; init; }

    /// <summary>
    /// Gets the maximum search results per query.
    /// </summary>
    public int Limit { get; init; } = 5;

    /// <summary>
    /// Gets the search mode to validate: full_text, semantic, or hybrid.
    /// </summary>
    public string SearchMode { get; init; } = "full_text";
}
