namespace CnvRegulation.Application.Contracts;

/// <summary>
/// One curated search-quality query.
/// </summary>
public sealed class SearchQualityQuery
{
    /// <summary>
    /// Gets the stable query identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the user-facing query text.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Gets terms where at least one should appear in returned evidence.
    /// </summary>
    public IReadOnlyList<string> ExpectedAnyTerms { get; init; } = [];

    /// <summary>
    /// Gets acceptable result sources.
    /// </summary>
    public IReadOnlyList<string> ExpectedSources { get; init; } = [];

    /// <summary>
    /// Gets the minimum expected result count.
    /// </summary>
    public int MinResults { get; init; } = 1;
}
