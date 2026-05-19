namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public interface IFinancialMetricInputMapper
{
    IReadOnlyList<FinancialMetric> MapToFinancialMetrics(
        FinancialMetricsValidationResult validationResult);
}
