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
    /// Gets whether duplicate chunks should be embedded.
    /// </summary>
    public bool IncludeDuplicates { get; init; }

    /// <summary>
    /// Gets whether non-searchable wrapper chunks should be embedded.
    /// </summary>
    public bool IncludeNonSearchable { get; init; }
}
