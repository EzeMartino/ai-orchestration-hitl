using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Validates search quality against a curated query set.
/// </summary>
public interface ISearchQualityValidationService
{
    /// <summary>
    /// Runs search-quality validation.
    /// </summary>
    /// <param name="request">The validation request.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The validation report.</returns>
    Task<SearchQualityValidationReport> ValidateAsync(
        ValidateSearchQualityRequest request,
        CancellationToken cancellationToken);
}
