namespace CnvRegulation.Domain;

/// <summary>
/// Represents a citable chunk extracted from a regulation document.
/// </summary>
public sealed class RegulationChunk
{
    /// <summary>
    /// Gets the stable chunk identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the source document identifier.
    /// </summary>
    public required string DocumentId { get; init; }

    /// <summary>
    /// Gets the current legal title marker when detected.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the current legal chapter marker when detected.
    /// </summary>
    public string? Chapter { get; init; }

    /// <summary>
    /// Gets the current legal section marker when detected.
    /// </summary>
    public string? Section { get; init; }

    /// <summary>
    /// Gets the article marker when detected.
    /// </summary>
    public string? Article { get; init; }

    /// <summary>
    /// Gets the zero-based chunk index within the document.
    /// </summary>
    public int ChunkIndex { get; init; }

    /// <summary>
    /// Gets the chunk text.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets the normalized content hash used for duplicate detection.
    /// </summary>
    public string? ContentHash { get; init; }

    /// <summary>
    /// Gets the canonical chunk identifier when this chunk duplicates another chunk.
    /// </summary>
    public string? DuplicateOfChunkId { get; init; }

    /// <summary>
    /// Gets additional metadata associated with the chunk.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Metadata { get; init; }
}
