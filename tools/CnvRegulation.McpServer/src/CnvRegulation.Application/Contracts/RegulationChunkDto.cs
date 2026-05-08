namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Data transfer object for a citable regulation chunk.
/// </summary>
public sealed class RegulationChunkDto
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
    /// Gets the legal title marker when detected.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the legal chapter marker when detected.
    /// </summary>
    public string? Chapter { get; init; }

    /// <summary>
    /// Gets the legal section marker when detected.
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
    /// Gets additional metadata associated with the chunk.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Metadata { get; init; }
}
