namespace Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;

public sealed record FinancialAnalysisAiReviewResult(
    string Summary,
    IReadOnlyList<FinancialAnalysisAiKeyFinding> KeyFindings,
    string RiskInterpretation,
    IReadOnlyList<FinancialAnalysisAiDataQualityNote> DataQualityNotes,
    IReadOnlyList<string> Limitations,
    bool UsedLlm,
    bool UsedFallback,
    string? Provider,
    string? Model,
    string? FailureReason
);
