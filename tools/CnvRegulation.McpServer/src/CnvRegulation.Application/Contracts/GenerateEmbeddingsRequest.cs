namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Request for generating missing regulation chunk embeddings.
/// </summary>
public sealed class GenerateEmbeddingsRequest
{
    /// <summary>
    /// Gets the embedding provider name.
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// Gets the embedding model override.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Gets the expected vector dimensions.
    /// </summary>
    public int? Dimensions { get; init; }

    /// <summary>
    /// Gets the maximum chunks to process. Null means all.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Gets whether the command should only report eligible chunks without persisting embeddings.
    /// </summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// Gets whether only chunks without embeddings should be processed.
    /// </summary>
    public bool OnlyMissing { get; init; }

    /// <summary>
    /// Gets the generation batch size.
    /// </summary>
    public int BatchSize { get; init; } = 32;

    /// <summary>
    /// Gets the delay between generation batches in milliseconds.
    /// </summary>
    public int DelayMs { get; init; }

    /// <summary>
    /// Gets the maximum input text length allowed before skipping a chunk.
    /// </summary>
    public int MaxInputCharacters { get; init; } = 24_000;

    /// <summary>
    /// Gets the optional JSON report path for failed or skipped embeddings.
    /// </summary>
    public string? FailedReportPath { get; init; }

    /// <summary>
    /// Gets whether duplicate chunks should be embedded.
    /// </summary>
    public bool IncludeDuplicates { get; init; }

    /// <summary>
    /// Gets whether non-searchable wrapper chunks should be embedded.
    /// </summary>
    public bool IncludeNonSearchable { get; init; }
}
