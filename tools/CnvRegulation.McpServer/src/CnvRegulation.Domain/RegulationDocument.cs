namespace CnvRegulation.Domain;

/// <summary>
/// Represents a regulatory document known to the CNV regulation tool.
/// </summary>
public sealed class RegulationDocument
{
    /// <summary>
    /// Gets the stable document identifier used by the MCP server.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the source that published or hosts the document.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets the document type, such as regulation, resolution, or notice.
    /// </summary>
    public required string DocumentType { get; init; }

    /// <summary>
    /// Gets the resolution number when the document is a resolution.
    /// </summary>
    public string? ResolutionNumber { get; init; }

    /// <summary>
    /// Gets the document title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the publication date when known.
    /// </summary>
    public DateOnly? PublicationDate { get; init; }

    /// <summary>
    /// Gets the effective date when known.
    /// </summary>
    public DateOnly? EffectiveDate { get; init; }

    /// <summary>
    /// Gets the source URL.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Gets the current status of the document metadata.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Gets whether the source must be manually reviewed before regulatory use.
    /// </summary>
    public bool RequiresReview { get; init; }

    /// <summary>
    /// Gets the document text or mock placeholder text for this MVP.
    /// </summary>
    public required string Text { get; init; }
}
