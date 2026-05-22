using System.Collections.Generic;

namespace Orchestration.Application.Agents.Legal.AiReview;

public sealed record LegalAnalysisReviewResult(
    string ReviewSummary,
    IReadOnlyList<PossibleRegulatoryReviewArea> PossibleRegulatoryReviewAreas,
    IReadOnlyList<LegalEvidenceReference> EvidenceReferences,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Limitations,
    bool UsedLlm,
    bool UsedFallback,
    string? Provider,
    string? Model,
    string? FailureReason
);
