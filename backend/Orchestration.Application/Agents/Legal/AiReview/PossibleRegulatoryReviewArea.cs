using System.Collections.Generic;

namespace Orchestration.Application.Agents.Legal.AiReview;

public sealed record PossibleRegulatoryReviewArea(
    string Title,
    string Description,
    string Severity,
    IReadOnlyList<string> RelatedFinancialSignals,
    IReadOnlyList<string> EvidenceCitations
);
