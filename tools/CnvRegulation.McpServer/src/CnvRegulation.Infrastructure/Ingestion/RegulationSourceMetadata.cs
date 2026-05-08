namespace CnvRegulation.Infrastructure.Ingestion;

/// <summary>
/// Metadata read from a local sidecar metadata file.
/// </summary>
public sealed class RegulationSourceMetadata
{
    /// <summary>
    /// Gets the document identifier.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// Gets the document source.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Gets the document type.
    /// </summary>
    public string? DocumentType { get; init; }

    /// <summary>
    /// Gets the resolution number.
    /// </summary>
    public string? ResolutionNumber { get; init; }

    /// <summary>
    /// Gets the document title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the publication date.
    /// </summary>
    public DateOnly? PublicationDate { get; init; }

    /// <summary>
    /// Gets the effective date.
    /// </summary>
    public DateOnly? EffectiveDate { get; init; }

    /// <summary>
    /// Gets the source URL.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    /// Gets the document status.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// Gets whether the source requires regulatory review before use.
    /// </summary>
    public bool? RequiresReview { get; init; }

    /// <summary>
    /// Gets when the source was retrieved.
    /// </summary>
    public DateTimeOffset? RetrievedAt { get; init; }
}
