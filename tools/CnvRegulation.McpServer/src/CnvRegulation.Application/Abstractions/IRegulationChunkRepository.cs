using CnvRegulation.Domain;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Stores and retrieves citable regulation chunks.
/// </summary>
public interface IRegulationChunkRepository
{
    /// <summary>
    /// Replaces chunks for the specified document.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="chunks">The chunks to store.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>A task that completes when chunks are stored.</returns>
    Task ReplaceForDocumentAsync(
        string documentId,
        IReadOnlyList<RegulationChunk> chunks,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists chunks for one document.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The stored chunks for the document.</returns>
    Task<IReadOnlyList<RegulationChunk>> ListByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists all stored chunks.
    /// </summary>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The stored chunks.</returns>
    Task<IReadOnlyList<RegulationChunk>> ListChunksAsync(CancellationToken cancellationToken);
}
