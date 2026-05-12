namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Computes stable hashes for normalized regulation chunk text.
/// </summary>
public interface IRegulationChunkHasher
{
    /// <summary>
    /// Computes a stable normalized text hash.
    /// </summary>
    /// <param name="text">The chunk text.</param>
    /// <returns>The hash value, or null when text is empty.</returns>
    string? ComputeHash(string text);
}
