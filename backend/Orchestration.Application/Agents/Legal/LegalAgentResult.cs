using Orchestration.Application.Agents.Legal.AiReview;

namespace Orchestration.Application.Agents.Legal;

public sealed record LegalAgentResult(
    bool HasComplianceRisk,
    string RiskLevel,
    string Summary,
    string Engine,
    IReadOnlyList<LegalEvidence> Evidence,
    IReadOnlyList<string> Warnings,
    object? QueryStrategy = null,
    LegalAnalysisReviewResult? LegalReview = null,
    bool RequiresHumanReview = false
);
