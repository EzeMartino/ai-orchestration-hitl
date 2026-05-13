namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Result from an embedding generator.
/// </summary>
public sealed class EmbeddingResult
{
    /// <summary>
    /// Gets the embedding vector.
    /// </summary>
    public required float[] Vector { get; init; }

    /// <summary>
    /// Gets the model used to produce the vector.
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// Gets the vector dimensions.
    /// </summary>
    public required int Dimensions { get; init; }

    /// <summary>
    /// Gets provider-reported token count when available.
    /// </summary>
    public int? TokenCount { get; init; }
}
