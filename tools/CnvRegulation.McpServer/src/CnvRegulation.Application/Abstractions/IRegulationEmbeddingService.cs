using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Generates missing embeddings for stored regulation chunks.
/// </summary>
public interface IRegulationEmbeddingService
{
    /// <summary>
    /// Generates embeddings for chunks that need them.
    /// </summary>
    /// <param name="request">The generation request.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The generation summary.</returns>
    Task<GenerateEmbeddingsResponse> GenerateMissingEmbeddingsAsync(
        GenerateEmbeddingsRequest request,
        CancellationToken cancellationToken);
}
