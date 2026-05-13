namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Per-query comparison result across search modes.
/// </summary>
public sealed class SearchQualityComparisonResult
{
    /// <summary>
    /// Gets the query identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the query text.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Gets whether the baseline passed.
    /// </summary>
    public bool BaselinePassed { get; init; }

    /// <summary>
    /// Gets whether the candidate passed.
    /// </summary>
    public bool CandidatePassed { get; init; }

    /// <summary>
    /// Gets baseline top result summary.
    /// </summary>
    public required string BaselineTopResult { get; init; }

    /// <summary>
    /// Gets baseline validation details.
    /// </summary>
    public SearchQualityValidationResult? BaselineResult { get; init; }

    /// <summary>
    /// Gets candidate top result summary.
    /// </summary>
    public required string CandidateTopResult { get; init; }

    /// <summary>
    /// Gets candidate validation details.
    /// </summary>
    public SearchQualityValidationResult? CandidateResult { get; init; }

    /// <summary>
    /// Gets whether top results differ.
    /// </summary>
    public bool TopResultChanged { get; init; }

    /// <summary>
    /// Gets candidate hybrid score breakdown when available.
    /// </summary>
    public string? CandidateScoreBreakdown { get; init; }

    /// <summary>
    /// Gets combined failure reasons.
    /// </summary>
    public required IReadOnlyList<string> FailureReasons { get; init; }
}
