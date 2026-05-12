namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Coverage diagnostics for ingested regulation documents and chunks.
/// </summary>
public sealed class RegulationCoverageReport
{
    /// <summary>
    /// Gets the total document count.
    /// </summary>
    public int DocumentsTotal { get; init; }

    /// <summary>
    /// Gets searchable document count.
    /// </summary>
    public int SearchableDocuments { get; init; }

    /// <summary>
    /// Gets non-searchable document count.
    /// </summary>
    public int NonSearchableDocuments { get; init; }

    /// <summary>
    /// Gets document distribution by source.
    /// </summary>
    public required IReadOnlyList<CoverageDistributionItem> SourceDistribution { get; init; }

    /// <summary>
    /// Gets document distribution by resolution number.
    /// </summary>
    public required IReadOnlyList<CoverageDistributionItem> ResolutionNumberDistribution { get; init; }

    /// <summary>
    /// Gets total chunk count.
    /// </summary>
    public int ChunksTotal { get; init; }

    /// <summary>
    /// Gets chunks with article identifiers.
    /// </summary>
    public int ChunksWithArticle { get; init; }

    /// <summary>
    /// Gets chunks without article identifiers.
    /// </summary>
    public int ChunksWithoutArticle { get; init; }

    /// <summary>
    /// Gets distinct article identifier count.
    /// </summary>
    public int DistinctArticleCount { get; init; }

    /// <summary>
    /// Gets possible duplicate URL count beyond the first document for each URL.
    /// </summary>
    public int DuplicateUrlCount { get; init; }

    /// <summary>
    /// Gets possible duplicate chunk count.
    /// </summary>
    public int DuplicateChunkCount { get; init; }

    /// <summary>
    /// Gets duplicate chunks hidden by default search behavior.
    /// </summary>
    public int DuplicateChunksHiddenByDefault { get; init; }

    /// <summary>
    /// Gets unique searchable chunk count after default duplicate and non-searchable filtering.
    /// </summary>
    public int UniqueSearchableChunks { get; init; }

    /// <summary>
    /// Gets very short chunk count.
    /// </summary>
    public int VeryShortChunks { get; init; }

    /// <summary>
    /// Gets very long chunk count.
    /// </summary>
    public int VeryLongChunks { get; init; }

    /// <summary>
    /// Gets document count with no stored chunks.
    /// </summary>
    public int DocumentsWithZeroChunks { get; init; }

    /// <summary>
    /// Gets documents that look like low-coverage Infoleg wrapper pages.
    /// </summary>
    public int PotentialWrapperDocuments { get; init; }

    /// <summary>
    /// Gets top coverage warnings.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
