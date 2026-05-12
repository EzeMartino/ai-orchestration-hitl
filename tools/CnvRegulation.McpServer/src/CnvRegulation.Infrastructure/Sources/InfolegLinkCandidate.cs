namespace CnvRegulation.Infrastructure.Sources;

/// <summary>
/// Represents one accepted Infoleg link candidate.
/// </summary>
public sealed class InfolegLinkCandidate
{
    /// <summary>
    /// Gets the canonical URL.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Gets the link classification.
    /// </summary>
    public InfolegLinkClassification Classification { get; init; }
}
