using System;

namespace Orchestration.Application.Agents.Legal.AiReview;

public static class LegalAnalysisReviewResults
{
    public static LegalAnalysisReviewResult NotRun(string reason)
    {
        return new LegalAnalysisReviewResult(
            ReviewSummary: "La revisión legal de IA no se ejecutó.",
            PossibleRegulatoryReviewAreas: Array.Empty<PossibleRegulatoryReviewArea>(),
            EvidenceReferences: Array.Empty<LegalEvidenceReference>(),
            Warnings: Array.Empty<string>(),
            Limitations: new[]
            {
                "La revisión legal de IA no se ejecutó.",
                "Este sistema no brinda asesoramiento legal.",
                "No se proporciona ninguna conclusión legal o regulatoria definitiva."
            },
            UsedLlm: false,
            UsedFallback: true,
            Provider: null,
            Model: null,
            FailureReason: reason
        );
    }
}
