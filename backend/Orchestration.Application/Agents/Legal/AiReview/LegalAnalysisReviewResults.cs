using System;

namespace Orchestration.Application.Agents.Legal.AiReview;

public static class LegalAnalysisReviewResults
{
    public static LegalAnalysisReviewResult NotRun(string reason)
    {
        return new LegalAnalysisReviewResult(
            ReviewSummary: "Legal AI review was not executed.",
            PossibleRegulatoryReviewAreas: Array.Empty<PossibleRegulatoryReviewArea>(),
            EvidenceReferences: Array.Empty<LegalEvidenceReference>(),
            Warnings: Array.Empty<string>(),
            Limitations: new[]
            {
                "Legal AI review was not executed.",
                "This system does not provide legal advice.",
                "No definitive legal or regulatory conclusion is provided."
            },
            UsedLlm: false,
            UsedFallback: true,
            Provider: null,
            Model: null,
            FailureReason: reason
        );
    }
}
