namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Request for searching CNV regulatory material.
/// </summary>
public sealed class SearchRegulationRequest
{
    /// <summary>
    /// Gets the natural language or keyword query.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Gets the optional regulatory area filter.
    /// </summary>
    public string? Area { get; init; }

    /// <summary>
    /// Gets the maximum number of results to return.
    /// </summary>
    public int Limit { get; init; } = 5;
}
