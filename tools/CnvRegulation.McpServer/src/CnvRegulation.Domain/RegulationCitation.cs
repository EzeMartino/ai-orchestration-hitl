namespace CnvRegulation.Domain;

/// <summary>
/// Represents a traceable citation to regulatory material.
/// </summary>
public sealed class RegulationCitation
{
    /// <summary>
    /// Gets the source that published or hosts the cited material.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets the cited document type.
    /// </summary>
    public required string DocumentType { get; init; }

    /// <summary>
    /// Gets the cited resolution number when available.
    /// </summary>
    public string? ResolutionNumber { get; init; }

    /// <summary>
    /// Gets the cited document title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the cited chapter when available.
    /// </summary>
    public string? Chapter { get; init; }

    /// <summary>
    /// Gets the cited section when available.
    /// </summary>
    public string? Section { get; init; }

    /// <summary>
    /// Gets the cited article when available.
    /// </summary>
    public string? Article { get; init; }

    /// <summary>
    /// Gets the publication date when known.
    /// </summary>
    public DateOnly? PublicationDate { get; init; }

    /// <summary>
    /// Gets the URL where the source material can be checked.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Gets the quoted text or mock placeholder text for this MVP.
    /// </summary>
    public required string QuotedText { get; init; }
}
