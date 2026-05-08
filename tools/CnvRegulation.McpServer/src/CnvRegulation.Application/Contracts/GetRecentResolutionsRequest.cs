namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Request for retrieving recent mock CNV resolutions.
/// </summary>
public sealed class GetRecentResolutionsRequest
{
    /// <summary>
    /// Gets the lookback window in days.
    /// </summary>
    public int Days { get; init; } = 30;

    /// <summary>
    /// Gets the optional source filter.
    /// </summary>
    public string? Source { get; init; }
}
