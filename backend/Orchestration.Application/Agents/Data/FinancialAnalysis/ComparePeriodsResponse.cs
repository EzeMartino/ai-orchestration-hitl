namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record ComparePeriodsResponse(
    string Engine,
    IReadOnlyList<FinancialPeriodComparison> Comparisons,
    IReadOnlyList<string> Warnings
);
