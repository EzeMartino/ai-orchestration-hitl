namespace CnvRegulation.Application.Search;

/// <summary>
/// Represents controlled expansion of a regulatory search query.
/// </summary>
public sealed class RegulationQueryExpansion
{
    /// <summary>
    /// Gets the original user query.
    /// </summary>
    public required string OriginalQuery { get; init; }

    /// <summary>
    /// Gets the normalized query used for alias detection.
    /// </summary>
    public required string NormalizedQuery { get; init; }

    /// <summary>
    /// Gets all expanded terms added by alias matching.
    /// </summary>
    public required IReadOnlyList<string> ExpandedTerms { get; init; }

    /// <summary>
    /// Gets bounded search queries to execute.
    /// </summary>
    public required IReadOnlyList<string> SearchQueries { get; init; }
}
