using Orchestration.Application.Agents.Shared;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public interface IStructuredFinancialMetricsProvider
{
    Task<StructuredFinancialMetricsDocument?> GetMetricsAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken);
}
