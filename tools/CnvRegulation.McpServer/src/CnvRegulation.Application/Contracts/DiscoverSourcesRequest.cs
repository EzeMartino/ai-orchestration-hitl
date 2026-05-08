namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Request for creating a curated source manifest.
/// </summary>
public sealed class DiscoverSourcesRequest
{
    /// <summary>
    /// Gets the target manifest path.
    /// </summary>
    public required string ManifestPath { get; init; }
}
