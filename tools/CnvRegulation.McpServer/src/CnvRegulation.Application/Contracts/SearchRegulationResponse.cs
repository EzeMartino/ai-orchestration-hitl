using CnvRegulation.Domain;

namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Response returned by CNV regulation search.
/// </summary>
public sealed class SearchRegulationResponse
{
    /// <summary>
    /// Gets the original query.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Gets the matched search results.
    /// </summary>
    public required IReadOnlyList<RegulationSearchResult> Results { get; init; }

    /// <summary>
    /// Gets warnings that qualify the response.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
