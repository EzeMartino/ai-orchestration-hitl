namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public interface IStructuredFinancialMetricsSessionService
{
    Task<FinancialMetricsSessionSaveResult?> StageAsync(
        SaveStructuredFinancialMetricsRequest request,
        CancellationToken cancellationToken);

    Task<FinancialMetricsSessionSaveResult?> SaveAsync(
        Guid sessionId,
        StructuredFinancialMetricsInput input,
        CancellationToken cancellationToken);

    Task<FinancialMetricsSessionSaveResult?> SaveAsync(
        SaveStructuredFinancialMetricsRequest request,
        CancellationToken cancellationToken);

    Task<StructuredFinancialMetricsContext?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken);
}
