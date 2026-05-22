namespace Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;

public interface IDataAgentAiReviewService
{
    Task<FinancialAnalysisAiReviewResult> ReviewAsync(
        FinancialAnalysisAiReviewInput input,
        CancellationToken cancellationToken);
}
