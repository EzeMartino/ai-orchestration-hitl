namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Response returned by source inspection diagnostics.
/// </summary>
public sealed class InspectSourcesResponse
{
    /// <summary>
    /// Gets the number of supported documents inspected.
    /// </summary>
    public int DocumentsInspected { get; init; }

    /// <summary>
    /// Gets the number of supported documents that produced chunks.
    /// </summary>
    public int DocumentsWithChunks { get; init; }

    /// <summary>
    /// Gets the number of supported documents without chunks.
    /// </summary>
    public int DocumentsWithoutChunks { get; init; }

    /// <summary>
    /// Gets the number of unsupported source files found.
    /// </summary>
    public int UnsupportedFiles { get; init; }

    /// <summary>
    /// Gets per-file diagnostics.
    /// </summary>
    public required IReadOnlyList<SourceInspectionDocumentResult> Documents { get; init; }

    /// <summary>
    /// Gets response-level warnings.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
