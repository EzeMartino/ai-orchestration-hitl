namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Controlled list of regulatory sources eligible for download.
/// </summary>
public sealed class SourceManifest
{
    /// <summary>
    /// Gets the manifest generation timestamp.
    /// </summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>
    /// Gets the source entries.
    /// </summary>
    public required IReadOnlyList<SourceManifestItem> Sources { get; init; }
}
