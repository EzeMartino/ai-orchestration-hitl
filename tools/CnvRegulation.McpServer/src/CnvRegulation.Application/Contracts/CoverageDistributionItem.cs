namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Count for one coverage distribution label.
/// </summary>
public sealed class CoverageDistributionItem
{
    /// <summary>
    /// Gets the distribution label.
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// Gets the number of items for this label.
    /// </summary>
    public int Count { get; init; }
}
