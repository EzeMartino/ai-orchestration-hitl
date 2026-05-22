namespace Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;

public sealed record SemanticKernelDataAgentAiReviewParsedResponse(
    string Summary,
    IReadOnlyList<FinancialAnalysisAiKeyFinding> KeyFindings,
    string RiskInterpretation,
    IReadOnlyList<FinancialAnalysisAiDataQualityNote> DataQualityNotes,
    IReadOnlyList<string> Limitations
);
