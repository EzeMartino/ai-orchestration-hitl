namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Normalizes extracted source text before chunking and search.
/// </summary>
public interface ITextNormalizer
{
    /// <summary>
    /// Normalizes extracted text.
    /// </summary>
    /// <param name="text">The extracted text.</param>
    /// <returns>The normalized text.</returns>
    string Normalize(string text);
}
