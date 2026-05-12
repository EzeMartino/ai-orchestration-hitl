namespace CnvRegulation.Domain;

/// <summary>
/// Represents one search hit returned by a regulation search.
/// </summary>
public sealed class RegulationSearchResult
{
    /// <summary>
    /// Gets the source document identifier.
    /// </summary>
    public required string DocumentId { get; init; }

    /// <summary>
    /// Gets the source chunk identifier.
    /// </summary>
    public required string ChunkId { get; init; }

    /// <summary>
    /// Gets the document title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the chapter label when available.
    /// </summary>
    public string? Chapter { get; init; }

    /// <summary>
    /// Gets the section label when available.
    /// </summary>
    public string? Section { get; init; }

    /// <summary>
    /// Gets the article label when available.
    /// </summary>
    public string? Article { get; init; }

    /// <summary>
    /// Gets the source that published or hosts the result.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets the source URL.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Gets a short result snippet.
    /// </summary>
    public required string Snippet { get; init; }

    /// <summary>
    /// Gets the mock relevance score.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Gets diagnostic metadata, such as hybrid score breakdown.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets citations that make the result traceable.
    /// </summary>
    public required IReadOnlyList<RegulationCitation> Citations { get; init; }
}
