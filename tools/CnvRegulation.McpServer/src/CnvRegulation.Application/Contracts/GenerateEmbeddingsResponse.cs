namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Summary of embedding generation.
/// </summary>
public sealed class GenerateEmbeddingsResponse
{
    /// <summary>
    /// Gets scanned chunk count.
    /// </summary>
    public int ChunksScanned { get; init; }

    /// <summary>
    /// Gets chunks missing embeddings before generation.
    /// </summary>
    public int MissingEmbeddings { get; init; }

    /// <summary>
    /// Gets generated embedding count.
    /// </summary>
    public int Generated { get; init; }

    /// <summary>
    /// Gets skipped duplicate chunk count.
    /// </summary>
    public int SkippedDuplicates { get; init; }

    /// <summary>
    /// Gets skipped non-searchable chunk count.
    /// </summary>
    public int SkippedNonSearchable { get; init; }

    /// <summary>
    /// Gets skipped empty chunk count.
    /// </summary>
    public int SkippedEmpty { get; init; }

    /// <summary>
    /// Gets provider name.
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Gets model name.
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// Gets vector dimensions.
    /// </summary>
    public int Dimensions { get; init; }

    /// <summary>
    /// Gets warnings.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
