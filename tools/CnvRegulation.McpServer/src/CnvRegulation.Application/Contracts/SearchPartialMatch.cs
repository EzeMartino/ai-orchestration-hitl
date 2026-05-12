namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Partial text match useful for search diagnostics.
/// </summary>
public sealed class SearchPartialMatch
{
    /// <summary>
    /// Gets the source document identifier.
    /// </summary>
    public required string DocumentId { get; init; }

    /// <summary>
    /// Gets the chunk identifier, when the match is chunk-level.
    /// </summary>
    public string? ChunkId { get; init; }

    /// <summary>
    /// Gets the publishing source.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets the document title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the article marker, when available.
    /// </summary>
    public string? Article { get; init; }

    /// <summary>
    /// Gets a simple token-overlap score.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Gets a short text snippet.
    /// </summary>
    public required string Snippet { get; init; }
}
