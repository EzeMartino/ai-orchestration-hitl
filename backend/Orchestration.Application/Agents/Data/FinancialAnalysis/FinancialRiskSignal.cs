namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record FinancialRiskSignal(
    string Name,
    string Severity,
    string Period,
    string Summary,
    IReadOnlyList<RiskEvidenceItem> Evidence
);
