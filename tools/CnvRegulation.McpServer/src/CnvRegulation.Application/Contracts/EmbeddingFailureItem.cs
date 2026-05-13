namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Describes one failed or skipped embedding generation item.
/// </summary>
public sealed class EmbeddingFailureItem
{
    /// <summary>
    /// Gets the source document id.
    /// </summary>
    public required string DocumentId { get; init; }

    /// <summary>
    /// Gets the chunk id.
    /// </summary>
    public required string ChunkId { get; init; }

    /// <summary>
    /// Gets the source name.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets the document title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the article identifier.
    /// </summary>
    public string? Article { get; init; }

    /// <summary>
    /// Gets the failure status.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Gets the failure reason.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Gets the chunk text length.
    /// </summary>
    public int TextLength { get; init; }

    /// <summary>
    /// Gets the estimated token count.
    /// </summary>
    public int EstimatedTokens { get; init; }
}
