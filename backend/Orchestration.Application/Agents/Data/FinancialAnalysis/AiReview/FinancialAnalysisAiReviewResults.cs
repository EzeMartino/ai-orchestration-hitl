namespace Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;

public static class FinancialAnalysisAiReviewResults
{
    public static FinancialAnalysisAiReviewResult NotRun(string reason)
    {
        return new FinancialAnalysisAiReviewResult(
            Summary: "La revisión de IA no se ejecutó.",
            KeyFindings: [],
            RiskInterpretation: "No hay interpretación de IA disponible.",
            DataQualityNotes: [],
            Limitations: ["La revisión de IA no se ejecutó."],
            UsedLlm: false,
            UsedFallback: true,
            Provider: null,
            Model: null,
            FailureReason: reason
        );
    }
}
