namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Represents one detected regulatory topic for evidence search.
/// </summary>
public sealed class RegulatoryTopic
{
    /// <summary>
    /// Gets the topic name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the query to run against CNV regulatory search.
    /// </summary>
    public required string SearchQuery { get; init; }

    /// <summary>
    /// Gets the deterministic topic confidence.
    /// </summary>
    public double Confidence { get; init; }
}
