namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Result of extracting text from a PDF source.
/// </summary>
public sealed class PdfExtractionResult
{
    /// <summary>
    /// Gets the complete extracted text.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets page-level extracted text.
    /// </summary>
    public required IReadOnlyList<PdfPageText> Pages { get; init; }

    /// <summary>
    /// Gets extraction warnings.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
