using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Searches CNV regulatory material.
/// </summary>
public interface IRegulationSearchService
{
    /// <summary>
    /// Searches regulatory material using the supplied request.
    /// </summary>
    /// <param name="request">The search request.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The search response.</returns>
    Task<SearchRegulationResponse> SearchAsync(
        SearchRegulationRequest request,
        CancellationToken cancellationToken);
}
