namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Response returned after local source ingestion.
/// </summary>
public sealed class IngestRegulationSourceResponse
{
    /// <summary>
    /// Gets the number of documents ingested.
    /// </summary>
    public int DocumentsIngested { get; init; }

    /// <summary>
    /// Gets the number of files skipped.
    /// </summary>
    public int DocumentsSkipped { get; init; }

    /// <summary>
    /// Gets warnings produced during ingestion.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
