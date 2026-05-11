namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Quality diagnostics for ingested regulation chunks.
/// </summary>
public sealed class ChunkQualityReport
{
    /// <summary>
    /// Gets the number of documents analyzed.
    /// </summary>
    public int DocumentsAnalyzed { get; init; }

    /// <summary>
    /// Gets the number of chunks analyzed.
    /// </summary>
    public int ChunksAnalyzed { get; init; }

    /// <summary>
    /// Gets the number of empty chunks.
    /// </summary>
    public int EmptyChunks { get; init; }

    /// <summary>
    /// Gets the number of very short chunks.
    /// </summary>
    public int VeryShortChunks { get; init; }

    /// <summary>
    /// Gets the number of very long chunks.
    /// </summary>
    public int VeryLongChunks { get; init; }

    /// <summary>
    /// Gets the number of chunks without article identifiers.
    /// </summary>
    public int ChunksWithoutArticle { get; init; }

    /// <summary>
    /// Gets the number of possible duplicate chunks.
    /// </summary>
    public int PossibleDuplicateChunks { get; init; }

    /// <summary>
    /// Gets the number of chunks with suspicious repeated header/footer text.
    /// </summary>
    public int SuspiciousHeaderFooterPollution { get; init; }

    /// <summary>
    /// Gets the number of suspicious chunks.
    /// </summary>
    public int SuspiciousChunks { get; init; }

    /// <summary>
    /// Gets the average chunk length.
    /// </summary>
    public double AverageChunkLength { get; init; }

    /// <summary>
    /// Gets the median chunk length.
    /// </summary>
    public double MedianChunkLength { get; init; }

    /// <summary>
    /// Gets quality warnings.
    /// </summary>
    public IReadOnlyList<ChunkQualityWarning> Warnings { get; init; } = [];
}
