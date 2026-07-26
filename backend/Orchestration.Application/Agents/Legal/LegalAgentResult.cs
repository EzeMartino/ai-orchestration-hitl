using Orchestration.Application.Agents.Legal.AiReview;
using Orchestration.Application.Agents.Legal.Regulations;

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
    bool RequiresHumanReview = false,
    RegulatoryEvidenceAssessment? EvidenceAssessment = null,
    IReadOnlyList<RegulatoryEvidenceEnrichment>? EvidenceEnrichments = null
);
