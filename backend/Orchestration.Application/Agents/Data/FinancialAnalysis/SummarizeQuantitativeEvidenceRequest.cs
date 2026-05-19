namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record SummarizeQuantitativeEvidenceRequest(
    IReadOnlyList<FinancialMetric> Metrics,
    IReadOnlyList<FinancialRatio> Ratios,
    IReadOnlyList<FinancialPeriodComparison> Comparisons,
    IReadOnlyList<FinancialRiskSignal> Signals,
    int MaxItems = 5
);
