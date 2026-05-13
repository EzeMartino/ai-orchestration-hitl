namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Search-quality comparison report across two modes.
/// </summary>
public sealed class SearchQualityComparisonReport
{
    /// <summary>
    /// Gets the query set path.
    /// </summary>
    public required string QuerySetPath { get; init; }

    /// <summary>
    /// Gets the baseline mode.
    /// </summary>
    public required string BaselineMode { get; init; }

    /// <summary>
    /// Gets the candidate mode.
    /// </summary>
    public required string CandidateMode { get; init; }

    /// <summary>
    /// Gets total query count.
    /// </summary>
    public int Queries { get; init; }

    /// <summary>
    /// Gets baseline passed count.
    /// </summary>
    public int BaselinePassed { get; init; }

    /// <summary>
    /// Gets baseline failed count.
    /// </summary>
    public int BaselineFailed { get; init; }

    /// <summary>
    /// Gets candidate passed count.
    /// </summary>
    public int CandidatePassed { get; init; }

    /// <summary>
    /// Gets candidate failed count.
    /// </summary>
    public int CandidateFailed { get; init; }

    /// <summary>
    /// Gets per-query comparison results.
    /// </summary>
    public required IReadOnlyList<SearchQualityComparisonResult> Results { get; init; }

    /// <summary>
    /// Gets warnings.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
