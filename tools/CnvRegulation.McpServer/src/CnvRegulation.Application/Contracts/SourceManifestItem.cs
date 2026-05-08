namespace CnvRegulation.Application.Contracts;

/// <summary>
/// One controlled regulatory source candidate.
/// </summary>
public sealed class SourceManifestItem
{
    /// <summary>
    /// Gets the source identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the source publisher.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets the document type.
    /// </summary>
    public required string DocumentType { get; init; }

    /// <summary>
    /// Gets the resolution number when known.
    /// </summary>
    public string? ResolutionNumber { get; init; }

    /// <summary>
    /// Gets the source title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the source URL.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Gets the intended local file name.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets the intended local metadata sidecar file name.
    /// </summary>
    public required string MetadataFileName { get; init; }

    /// <summary>
    /// Gets the source status.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Gets the source priority.
    /// </summary>
    public required string Priority { get; init; }

    /// <summary>
    /// Gets whether the source requires regulatory review before use.
    /// </summary>
    public bool RequiresReview { get; init; }

    /// <summary>
    /// Gets the publication date when known.
    /// </summary>
    public DateOnly? PublicationDate { get; init; }

    /// <summary>
    /// Gets the effective date when known.
    /// </summary>
    public DateOnly? EffectiveDate { get; init; }
}
