using Orchestration.Application.Agents.Legal.AiReview;

namespace Orchestration.Application.Agents.Legal.Regulations;

public sealed record RegulatoryReviewResult(
    bool HasComplianceRisk,
    string RiskLevel,
    string Summary,
    string SourceEngine,
    IReadOnlyList<RegulatoryFinding> Findings,
    IReadOnlyList<string> Warnings,
    object? QueryStrategy = null,
    LegalAnalysisReviewResult? LegalReview = null,
    bool RequiresHumanReview = false,
    RegulatoryEvidenceAssessment? EvidenceAssessment = null,
    IReadOnlyList<RegulatoryEvidenceEnrichment>? EvidenceEnrichments = null
);
