namespace CnvRegulation.Application.Contracts;

/// <summary>
/// JSON payload for failed embedding generation reports.
/// </summary>
public sealed class EmbeddingFailureReport
{
    /// <summary>
    /// Gets report generation timestamp.
    /// </summary>
    public DateTimeOffset GeneratedAt { get; init; }

    /// <summary>
    /// Gets the embedding provider.
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Gets the embedding model.
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// Gets total failed item count.
    /// </summary>
    public int Failed { get; init; }

    /// <summary>
    /// Gets failed items.
    /// </summary>
    public required IReadOnlyList<EmbeddingFailureItem> Items { get; init; }
}
