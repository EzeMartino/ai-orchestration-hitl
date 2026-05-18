namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record DetectFinancialRiskSignalsResponse(
    string Engine,
    IReadOnlyList<FinancialRiskSignal> Signals,
    FinancialAnalysisToolResult Result
);
