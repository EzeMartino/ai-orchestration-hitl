namespace Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;

public sealed record FinancialAnalysisAiDataQualityNote(
    string Message,
    string Severity,
    IReadOnlyList<string> RelatedFields
);
