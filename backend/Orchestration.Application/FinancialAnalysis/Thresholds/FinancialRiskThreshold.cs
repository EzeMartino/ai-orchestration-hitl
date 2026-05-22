namespace Orchestration.Application.FinancialAnalysis.Thresholds;

public sealed record FinancialRiskThreshold(
    string Code,
    string Metric,
    string Operator,
    decimal Value,
    string Severity,
    string Description
);
