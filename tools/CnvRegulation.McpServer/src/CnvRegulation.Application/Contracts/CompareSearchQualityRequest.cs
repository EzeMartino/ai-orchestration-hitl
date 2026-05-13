namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Request for comparing search-quality modes.
/// </summary>
public sealed class CompareSearchQualityRequest
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
    /// Gets search modes to compare.
    /// </summary>
    public required IReadOnlyList<string> Modes { get; init; }
}
