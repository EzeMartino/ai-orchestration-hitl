namespace Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;

public static class FinancialAnalysisAiReviewSafetyConstraints
{
    public const string AdvisoryOnly = "The AI review is advisory.";

    public const string DoNotRecomputeMetrics = "The AI review must not recompute financial metrics.";

    public const string NoInvestmentAdvice = "The AI review must not provide investment advice.";

    public const string NoAccountingCorrectnessClaim = "The AI review must not claim accounting correctness.";

    public const string InterpretProvidedEvidenceOnly = "The AI review must only interpret provided evidence.";
}
