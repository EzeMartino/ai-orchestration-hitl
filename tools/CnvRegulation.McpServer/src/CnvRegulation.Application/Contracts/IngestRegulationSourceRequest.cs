namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Request for ingesting local regulation source files.
/// </summary>
public sealed class IngestRegulationSourceRequest
{
    /// <summary>
    /// Gets the directory containing source files and sidecar metadata.
    /// </summary>
    public required string SourceDirectory { get; init; }
}
