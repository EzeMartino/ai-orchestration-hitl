namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Diagnostic report for a single regulatory search query.
/// </summary>
public sealed class ExplainSearchQueryReport
{
    /// <summary>
    /// Gets the original user query.
    /// </summary>
    public required string OriginalQuery { get; init; }

    /// <summary>
    /// Gets the normalized query.
    /// </summary>
    public required string NormalizedQuery { get; init; }

    /// <summary>
    /// Gets expanded terms produced by alias expansion.
    /// </summary>
    public required IReadOnlyList<string> ExpandedTerms { get; init; }

    /// <summary>
    /// Gets generated search queries and their raw result counts.
    /// </summary>
    public required IReadOnlyList<GeneratedSearchQueryDiagnostic> GeneratedQueries { get; init; }

    /// <summary>
    /// Gets indexed term presence diagnostics.
    /// </summary>
    public required IReadOnlyList<SearchTermPresence> TermPresence { get; init; }

    /// <summary>
    /// Gets top partial matches when exact search is weak or empty.
    /// </summary>
    public required IReadOnlyList<SearchPartialMatch> TopPartialMatches { get; init; }

    /// <summary>
    /// Gets the number of duplicate results hidden by default filtering.
    /// </summary>
    public int HiddenDuplicateResults { get; init; }

    /// <summary>
    /// Gets the number of non-searchable wrapper results hidden by default filtering.
    /// </summary>
    public int HiddenNonSearchableResults { get; init; }

    /// <summary>
    /// Gets filters applied to the diagnostic search.
    /// </summary>
    public required IReadOnlyList<string> FiltersApplied { get; init; }

    /// <summary>
    /// Gets diagnostic warnings.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>
    /// Gets a short recommended next action.
    /// </summary>
    public required string Recommendation { get; init; }
}
