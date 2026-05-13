namespace CnvRegulation.Application.Contracts;

/// <summary>
/// One mode result for manual search review.
/// </summary>
public sealed class SearchQualityReviewModeResult
{
    /// <summary>
    /// Gets whether expectations passed.
    /// </summary>
    public bool Passed { get; init; }

    /// <summary>
    /// Gets top result.
    /// </summary>
    public SearchQualityReviewTopResult? TopResult { get; init; }

    /// <summary>
    /// Gets warnings.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
