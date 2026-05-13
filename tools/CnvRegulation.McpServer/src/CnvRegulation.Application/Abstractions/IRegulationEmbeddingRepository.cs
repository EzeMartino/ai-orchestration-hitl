namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Persists regulation chunk embeddings.
/// </summary>
public interface IRegulationEmbeddingRepository
{
    /// <summary>
    /// Updates embedding metadata for one chunk.
    /// </summary>
    /// <param name="chunkId">The chunk identifier.</param>
    /// <param name="vector">The embedding vector.</param>
    /// <param name="model">The embedding model.</param>
    /// <param name="generatedAt">The generation timestamp.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>A task that completes when the embedding is persisted.</returns>
    Task UpdateChunkEmbeddingAsync(
        string chunkId,
        IReadOnlyList<float> vector,
        string model,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates embedding status metadata for one chunk.
    /// </summary>
    /// <param name="chunkId">The chunk identifier.</param>
    /// <param name="status">The embedding status.</param>
    /// <param name="reason">The optional status reason.</param>
    /// <param name="updatedAt">The status update timestamp.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>A task that completes when the status is persisted.</returns>
    Task UpdateChunkEmbeddingStatusAsync(
        string chunkId,
        string status,
        string? reason,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken);
}
