using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Inspects stored regulation coverage after ingestion.
/// </summary>
public interface IRegulationCoverageInspectionService
{
    /// <summary>
    /// Produces a coverage report from the configured repositories.
    /// </summary>
    /// <param name="request">The coverage inspection request.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The coverage report.</returns>
    Task<RegulationCoverageReport> InspectAsync(
        InspectCoverageRequest request,
        CancellationToken cancellationToken);
}
