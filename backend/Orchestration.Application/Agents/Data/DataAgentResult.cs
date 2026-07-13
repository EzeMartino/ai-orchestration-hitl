using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Application.Agents.Data;

public sealed record DataAgentResult(
    bool HasAnomaly,
    string Severity,
    string Summary,
    string Engine,
    IReadOnlyList<AnomalyEvidence> Evidence,
    FinancialAnalysisContext? FinancialAnalysis = null,
    bool RequiresHumanReview = false
);
