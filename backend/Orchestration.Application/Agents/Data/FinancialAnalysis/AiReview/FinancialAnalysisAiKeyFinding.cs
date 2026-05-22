namespace Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;

public sealed record FinancialAnalysisAiKeyFinding(
    string Title,
    string Description,
    string Severity,
    IReadOnlyList<string> RelatedMetrics
);
