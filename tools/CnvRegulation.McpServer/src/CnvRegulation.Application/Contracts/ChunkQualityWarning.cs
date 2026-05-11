namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Warning emitted by chunk quality diagnostics.
/// </summary>
public sealed class ChunkQualityWarning
{
    /// <summary>
    /// Gets the document identifier.
    /// </summary>
    public required string DocumentId { get; init; }

    /// <summary>
    /// Gets the chunk identifier.
    /// </summary>
    public required string ChunkId { get; init; }

    /// <summary>
    /// Gets the article identifier when available.
    /// </summary>
    public string? Article { get; init; }

    /// <summary>
    /// Gets the warning type.
    /// </summary>
    public required string WarningType { get; init; }

    /// <summary>
    /// Gets the warning message.
    /// </summary>
    public required string Message { get; init; }
}
