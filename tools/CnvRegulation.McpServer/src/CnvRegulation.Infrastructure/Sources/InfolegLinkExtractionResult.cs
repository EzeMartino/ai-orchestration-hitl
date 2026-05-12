namespace CnvRegulation.Infrastructure.Sources;

/// <summary>
/// Result of extracting Infoleg links from one HTML source.
/// </summary>
public sealed class InfolegLinkExtractionResult
{
    /// <summary>
    /// Gets accepted link candidates.
    /// </summary>
    public required IReadOnlyList<InfolegLinkCandidate> Candidates { get; init; }

    /// <summary>
    /// Gets the number of links discovered before filtering.
    /// </summary>
    public int LinksDiscovered { get; init; }

    /// <summary>
    /// Gets the number of links skipped by validation or deduplication.
    /// </summary>
    public int LinksSkipped { get; init; }
}
