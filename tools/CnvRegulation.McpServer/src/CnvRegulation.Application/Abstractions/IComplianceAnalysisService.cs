using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Performs mock compliance analysis against CNV material.
/// </summary>
public interface IComplianceAnalysisService
{
    /// <summary>
    /// Analyzes text against mock CNV material.
    /// </summary>
    /// <param name="request">The analysis request.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The analysis response.</returns>
    Task<AnalyzeTextAgainstCnvResponse> AnalyzeAsync(
        AnalyzeTextAgainstCnvRequest request,
        CancellationToken cancellationToken);
}
