namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Request for downloading sources from a manifest.
/// </summary>
public sealed class DownloadSourcesRequest
{
    /// <summary>
    /// Gets the path to the source manifest.
    /// </summary>
    public required string ManifestPath { get; init; }

    /// <summary>
    /// Gets the directory where sources and metadata sidecars should be written.
    /// </summary>
    public required string OutputDirectory { get; init; }
}
