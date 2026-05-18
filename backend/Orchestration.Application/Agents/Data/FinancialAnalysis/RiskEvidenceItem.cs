namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record RiskEvidenceItem(
    string MetricName,
    string Period,
    decimal Value,
    decimal? Threshold,
    string Unit,
    string Interpretation
);
