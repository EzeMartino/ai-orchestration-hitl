namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Response returned after creating a curated source manifest.
/// </summary>
public sealed class DiscoverSourcesResponse
{
    /// <summary>
    /// Gets the manifest path that was written.
    /// </summary>
    public required string ManifestPath { get; init; }

    /// <summary>
    /// Gets the number of source entries discovered.
    /// </summary>
    public int SourcesDiscovered { get; init; }

    /// <summary>
    /// Gets warnings produced during discovery.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
