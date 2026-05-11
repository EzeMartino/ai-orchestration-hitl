namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Extracted text for one PDF page.
/// </summary>
public sealed class PdfPageText
{
    /// <summary>
    /// Gets the one-based page number.
    /// </summary>
    public int PageNumber { get; init; }

    /// <summary>
    /// Gets the extracted page text.
    /// </summary>
    public required string Text { get; init; }
}
