using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Explains how a regulatory search query is normalized, expanded, and matched.
/// </summary>
public interface IExplainSearchQueryService
{
    /// <summary>
    /// Builds diagnostics for one search query.
    /// </summary>
    /// <param name="request">The query explanation request.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The query explanation report.</returns>
    Task<ExplainSearchQueryReport> ExplainAsync(
        ExplainSearchQueryRequest request,
        CancellationToken cancellationToken);
}
