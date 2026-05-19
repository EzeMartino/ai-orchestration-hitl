namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public interface IPythonFinancialAnalysisService
{
    Task<ComputeFinancialRatiosResponse> ComputeFinancialRatiosAsync(
        ComputeFinancialRatiosRequest request,
        CancellationToken cancellationToken);

    Task<ComparePeriodsResponse> ComparePeriodsAsync(
        ComparePeriodsRequest request,
        CancellationToken cancellationToken);

    Task<DetectFinancialRiskSignalsResponse> DetectFinancialRiskSignalsAsync(
        DetectFinancialRiskSignalsRequest request,
        CancellationToken cancellationToken);

    Task<SummarizeQuantitativeEvidenceResponse> SummarizeQuantitativeEvidenceAsync(
        SummarizeQuantitativeEvidenceRequest request,
        CancellationToken cancellationToken);
}
