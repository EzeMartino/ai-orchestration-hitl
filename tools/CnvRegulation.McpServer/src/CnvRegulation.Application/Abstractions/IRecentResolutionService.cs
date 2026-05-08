using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Retrieves recent CNV resolution metadata.
/// </summary>
public interface IRecentResolutionService
{
    /// <summary>
    /// Retrieves recent mock resolution items.
    /// </summary>
    /// <param name="request">The recent resolution request.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The recent resolution response.</returns>
    Task<GetRecentResolutionsResponse> GetRecentAsync(
        GetRecentResolutionsRequest request,
        CancellationToken cancellationToken);
}
