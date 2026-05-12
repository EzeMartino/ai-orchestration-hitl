namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Request for controlled Infoleg link discovery.
/// </summary>
public sealed class InfolegLinkDiscoveryRequest
{
    /// <summary>
    /// Gets the local source directory containing downloaded Infoleg HTML files.
    /// </summary>
    public required string SourceDirectory { get; init; }

    /// <summary>
    /// Gets the output manifest path.
    /// </summary>
    public required string OutputManifestPath { get; init; }

    /// <summary>
    /// Gets the maximum accepted links per source file.
    /// </summary>
    public int MaxLinksPerSource { get; init; } = 20;
}
