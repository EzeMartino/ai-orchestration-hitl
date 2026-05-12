namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Search-quality validation report.
/// </summary>
public sealed class SearchQualityValidationReport
{
    /// <summary>
    /// Gets the query set path.
    /// </summary>
    public required string QuerySetPath { get; init; }

    /// <summary>
    /// Gets total query count.
    /// </summary>
    public int Queries { get; init; }

    /// <summary>
    /// Gets passed query count.
    /// </summary>
    public int Passed { get; init; }

    /// <summary>
    /// Gets failed query count.
    /// </summary>
    public int Failed { get; init; }

    /// <summary>
    /// Gets per-query results.
    /// </summary>
    public required IReadOnlyList<SearchQualityValidationResult> Results { get; init; }

    /// <summary>
    /// Gets report-level warnings.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
