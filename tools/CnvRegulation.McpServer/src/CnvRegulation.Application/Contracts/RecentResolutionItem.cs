namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Represents one recent mock resolution item.
/// </summary>
public sealed class RecentResolutionItem
{
    /// <summary>
    /// Gets the document identifier.
    /// </summary>
    public required string DocumentId { get; init; }

    /// <summary>
    /// Gets the resolution title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the source name.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets the resolution number when available.
    /// </summary>
    public string? ResolutionNumber { get; init; }

    /// <summary>
    /// Gets the publication date when available.
    /// </summary>
    public DateOnly? PublicationDate { get; init; }

    /// <summary>
    /// Gets the source URL.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Gets a mock summary of the resolution.
    /// </summary>
    public required string Summary { get; init; }
}
