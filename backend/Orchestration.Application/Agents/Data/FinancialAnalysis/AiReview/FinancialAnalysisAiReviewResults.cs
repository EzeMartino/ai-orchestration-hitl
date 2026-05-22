namespace Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;

public static class FinancialAnalysisAiReviewResults
{
    public static FinancialAnalysisAiReviewResult NotRun(string reason)
    {
        return new FinancialAnalysisAiReviewResult(
            Summary: "AI review was not executed.",
            KeyFindings: [],
            RiskInterpretation: "No AI interpretation is available.",
            DataQualityNotes: [],
            Limitations: ["AI review was not executed."],
            UsedLlm: false,
            UsedFallback: true,
            Provider: null,
            Model: null,
            FailureReason: reason
        );
    }
}
