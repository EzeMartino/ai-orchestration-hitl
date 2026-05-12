namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Response returned by controlled Infoleg link discovery.
/// </summary>
public sealed class InfolegLinkDiscoveryResponse
{
    /// <summary>
    /// Gets the number of Infoleg HTML files inspected.
    /// </summary>
    public int InfolegFilesInspected { get; init; }

    /// <summary>
    /// Gets the number of links found before filtering.
    /// </summary>
    public int LinksDiscovered { get; init; }

    /// <summary>
    /// Gets the number of accepted official Infoleg links.
    /// </summary>
    public int LinksAccepted { get; init; }

    /// <summary>
    /// Gets the number of skipped links.
    /// </summary>
    public int LinksSkipped { get; init; }

    /// <summary>
    /// Gets the manifest path written by discovery.
    /// </summary>
    public required string ManifestPath { get; init; }

    /// <summary>
    /// Gets warnings emitted during discovery.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
