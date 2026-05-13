namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Summary for a search-quality review report.
/// </summary>
public sealed class SearchQualityReviewSummary
{
    /// <summary>
    /// Gets total query count.
    /// </summary>
    public int Queries { get; init; }

    /// <summary>
    /// Gets full-text passed count.
    /// </summary>
    public int FullTextPassed { get; init; }

    /// <summary>
    /// Gets hybrid passed count.
    /// </summary>
    public int HybridPassed { get; init; }

    /// <summary>
    /// Gets top-result changed count.
    /// </summary>
    public int TopResultChanged { get; init; }
}
