namespace Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;

public sealed record SemanticKernelDataAgentAiReviewParseResult(
    bool Succeeded,
    SemanticKernelDataAgentAiReviewParsedResponse? Response,
    string? FailureReason
);
