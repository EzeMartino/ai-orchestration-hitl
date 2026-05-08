using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Retrieves structured regulatory article content.
/// </summary>
public interface IRegulationArticleService
{
    /// <summary>
    /// Retrieves a regulatory article placeholder.
    /// </summary>
    /// <param name="request">The article request.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The article response.</returns>
    Task<GetRegulationArticleResponse> GetArticleAsync(
        GetRegulationArticleRequest request,
        CancellationToken cancellationToken);
}
