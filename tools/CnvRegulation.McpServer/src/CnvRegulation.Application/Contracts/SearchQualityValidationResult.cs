namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Per-query search-quality validation result.
/// </summary>
public sealed class SearchQualityValidationResult
{
    /// <summary>
    /// Gets the query identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the query text.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Gets expanded terms.
    /// </summary>
    public required IReadOnlyList<string> ExpandedTerms { get; init; }

    /// <summary>
    /// Gets expanded search queries.
    /// </summary>
    public required IReadOnlyList<string> ExpandedQueries { get; init; }

    /// <summary>
    /// Gets result count.
    /// </summary>
    public int ResultCount { get; init; }

    /// <summary>
    /// Gets the top result source.
    /// </summary>
    public string? TopResultSource { get; init; }

    /// <summary>
    /// Gets the top result title.
    /// </summary>
    public string? TopResultTitle { get; init; }

    /// <summary>
    /// Gets the top result article.
    /// </summary>
    public string? TopResultArticle { get; init; }

    /// <summary>
    /// Gets the top result score.
    /// </summary>
    public double? TopResultScore { get; init; }

    /// <summary>
    /// Gets top result diagnostic metadata.
    /// </summary>
    public IReadOnlyDictionary<string, string> TopResultMetadata { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets whether citations were present.
    /// </summary>
    public bool CitationsPresent { get; init; }

    /// <summary>
    /// Gets search warnings.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>
    /// Gets whether the query passed expectations.
    /// </summary>
    public bool Passed { get; init; }

    /// <summary>
    /// Gets failure reasons.
    /// </summary>
    public required IReadOnlyList<string> FailureReasons { get; init; }
}
