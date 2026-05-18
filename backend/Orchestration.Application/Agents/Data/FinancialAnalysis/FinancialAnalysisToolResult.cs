namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record FinancialAnalysisToolResult(
    bool HasRiskSignals,
    string RiskLevel,
    string Summary,
    string Engine,
    IReadOnlyList<RiskEvidenceItem> Evidence,
    IReadOnlyList<string> Warnings
);
