using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Generates semantic embeddings for text.
/// </summary>
public interface IEmbeddingGenerator
{
    /// <summary>
    /// Generates one embedding vector.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The generated embedding.</returns>
    Task<EmbeddingResult> GenerateAsync(
        string text,
        CancellationToken cancellationToken);
}
