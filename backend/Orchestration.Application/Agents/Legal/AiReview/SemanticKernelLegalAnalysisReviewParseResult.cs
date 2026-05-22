namespace Orchestration.Application.Agents.Legal.AiReview;

public sealed record SemanticKernelLegalAnalysisReviewParseResult(
    bool Succeeded,
    SemanticKernelLegalAnalysisReviewParsedResponse? Response,
    string? FailureReason
);
