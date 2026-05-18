namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record DetectFinancialRiskSignalsRequest(
    IReadOnlyList<FinancialMetric> Metrics,
    IReadOnlyList<FinancialRatio> Ratios,
    IReadOnlyList<FinancialPeriodComparison> Comparisons
);
