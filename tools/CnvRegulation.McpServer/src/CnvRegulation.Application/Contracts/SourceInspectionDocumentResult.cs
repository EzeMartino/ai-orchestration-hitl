namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Diagnostics for one local source file.
/// </summary>
public sealed class SourceInspectionDocumentResult
{
    /// <summary>
    /// Gets the source file name.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets the source name from sidecar metadata when available.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Gets the document title from sidecar metadata when available.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the document type from sidecar metadata when available.
    /// </summary>
    public string? DocumentType { get; init; }

    /// <summary>
    /// Gets the extracted text length.
    /// </summary>
    public int ExtractedTextLength { get; init; }

    /// <summary>
    /// Gets the number of chunks detected.
    /// </summary>
    public int ChunkCount { get; init; }

    /// <summary>
    /// Gets detected legal title markers.
    /// </summary>
    public required IReadOnlyList<string> DetectedTitles { get; init; }

    /// <summary>
    /// Gets detected chapter markers.
    /// </summary>
    public required IReadOnlyList<string> DetectedChapters { get; init; }

    /// <summary>
    /// Gets detected section markers.
    /// </summary>
    public required IReadOnlyList<string> DetectedSections { get; init; }

    /// <summary>
    /// Gets detected article markers.
    /// </summary>
    public required IReadOnlyList<string> DetectedArticles { get; init; }

    /// <summary>
    /// Gets the first detected articles for quick review.
    /// </summary>
    public required IReadOnlyList<string> FirstArticles { get; init; }

    /// <summary>
    /// Gets whether the file is unsupported by ingestion/parsing.
    /// </summary>
    public bool IsUnsupported { get; init; }

    /// <summary>
    /// Gets warnings for this source file.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
