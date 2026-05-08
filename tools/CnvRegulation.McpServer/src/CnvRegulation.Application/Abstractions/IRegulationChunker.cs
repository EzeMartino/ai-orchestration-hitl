using CnvRegulation.Domain;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Splits regulation documents into citable chunks.
/// </summary>
public interface IRegulationChunker
{
    /// <summary>
    /// Splits a document into regulation chunks.
    /// </summary>
    /// <param name="document">The document to chunk.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The detected chunks.</returns>
    Task<IReadOnlyList<RegulationChunk>> ChunkAsync(
        RegulationDocument document,
        CancellationToken cancellationToken);
}
