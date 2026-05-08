namespace CnvRegulation.Infrastructure.Sources;

internal sealed class DownloadedSourceMetadata
{
    public required string Id { get; init; }

    public required string Source { get; init; }

    public required string DocumentType { get; init; }

    public string? ResolutionNumber { get; init; }

    public required string Title { get; init; }

    public DateOnly? PublicationDate { get; init; }

    public DateOnly? EffectiveDate { get; init; }

    public required string Url { get; init; }

    public required string Status { get; init; }

    public bool RequiresReview { get; init; }

    public required DateTimeOffset RetrievedAt { get; init; }
}
