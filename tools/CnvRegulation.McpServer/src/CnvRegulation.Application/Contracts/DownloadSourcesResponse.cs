namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Response returned after downloading manifest sources.
/// </summary>
public sealed class DownloadSourcesResponse
{
    /// <summary>
    /// Gets the number of sources downloaded.
    /// </summary>
    public int SourcesDownloaded { get; init; }

    /// <summary>
    /// Gets the number of sources skipped.
    /// </summary>
    public int SourcesSkipped { get; init; }

    /// <summary>
    /// Gets warnings produced during download.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
