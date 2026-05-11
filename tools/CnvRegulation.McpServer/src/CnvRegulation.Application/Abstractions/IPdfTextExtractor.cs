using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Extracts text from local PDF sources.
/// </summary>
public interface IPdfTextExtractor
{
    /// <summary>
    /// Extracts page-aware text from a PDF file.
    /// </summary>
    /// <param name="filePath">The local PDF file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The PDF extraction result.</returns>
    Task<PdfExtractionResult> ExtractAsync(string filePath, CancellationToken cancellationToken);
}
