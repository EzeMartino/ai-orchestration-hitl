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

    /// <summary>
    /// Gets the optional source filter.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Gets the optional document type filter.
    /// </summary>
    public string? DocumentType { get; init; }

    /// <summary>
    /// Gets the optional resolution number filter.
    /// </summary>
    public string? ResolutionNumber { get; init; }

    /// <summary>
    /// Gets the optional document status filter.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// Gets the optional manual review requirement filter.
    /// </summary>
    public bool? RequiresReview { get; init; }

    /// <summary>
    /// Gets whether duplicate chunks should be returned.
    /// </summary>
    public bool IncludeDuplicates { get; init; }

    /// <summary>
    /// Gets whether non-searchable wrapper-like documents should be returned.
    /// </summary>
    public bool IncludeNonSearchable { get; init; }
}
