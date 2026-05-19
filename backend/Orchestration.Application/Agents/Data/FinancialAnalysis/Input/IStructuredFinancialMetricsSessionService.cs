namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public interface IStructuredFinancialMetricsSessionService
{
    Task<FinancialMetricsSessionSaveResult?> SaveAsync(
        Guid sessionId,
        StructuredFinancialMetricsInput input,
        CancellationToken cancellationToken);

    Task<StructuredFinancialMetricsContext?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken);
}
