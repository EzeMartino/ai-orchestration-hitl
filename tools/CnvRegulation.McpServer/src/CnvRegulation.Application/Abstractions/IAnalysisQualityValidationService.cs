using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Validates regulatory-analysis quality against curated text scenarios.
/// </summary>
public interface IAnalysisQualityValidationService
{
    /// <summary>
    /// Runs regulatory-analysis quality validation.
    /// </summary>
    /// <param name="request">The validation request.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The validation report.</returns>
    Task<AnalysisQualityValidationReport> ValidateAsync(
        ValidateAnalysisQualityRequest request,
        CancellationToken cancellationToken);
}
