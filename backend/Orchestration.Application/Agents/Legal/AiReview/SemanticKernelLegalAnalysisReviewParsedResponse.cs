using System.Collections.Generic;

namespace Orchestration.Application.Agents.Legal.AiReview;

public sealed record SemanticKernelLegalAnalysisReviewParsedResponse(
    string ReviewSummary,
    IReadOnlyList<PossibleRegulatoryReviewArea> PossibleRegulatoryReviewAreas,
    IReadOnlyList<LegalEvidenceReference> EvidenceReferences,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Limitations
);
