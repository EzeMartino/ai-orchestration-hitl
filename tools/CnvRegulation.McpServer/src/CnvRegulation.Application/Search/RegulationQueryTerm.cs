namespace CnvRegulation.Application.Search;

/// <summary>
/// Represents one normalized query term and its configured aliases.
/// </summary>
public sealed class RegulationQueryTerm
{
    /// <summary>
    /// Gets the alias text that matched the query.
    /// </summary>
    public required string Alias { get; init; }

    /// <summary>
    /// Gets the normalized alias.
    /// </summary>
    public required string NormalizedAlias { get; init; }

    /// <summary>
    /// Gets expanded terms for the alias.
    /// </summary>
    public required IReadOnlyList<string> ExpandedTerms { get; init; }
}
