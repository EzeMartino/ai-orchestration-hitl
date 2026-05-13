namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Regulatory-analysis quality validation report.
/// </summary>
public sealed class AnalysisQualityValidationReport
{
    /// <summary>
    /// Gets the case set path.
    /// </summary>
    public required string CaseSetPath { get; init; }

    /// <summary>
    /// Gets whether hybrid search was enabled for the analysis run.
    /// </summary>
    public bool UseHybridSearch { get; init; }

    /// <summary>
    /// Gets total case count.
    /// </summary>
    public int Cases { get; init; }

    /// <summary>
    /// Gets passed case count.
    /// </summary>
    public int Passed { get; init; }

    /// <summary>
    /// Gets failed case count.
    /// </summary>
    public int Failed { get; init; }

    /// <summary>
    /// Gets per-case validation results.
    /// </summary>
    public required IReadOnlyList<AnalysisQualityValidationResult> Results { get; init; }

    /// <summary>
    /// Gets report-level warnings.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
