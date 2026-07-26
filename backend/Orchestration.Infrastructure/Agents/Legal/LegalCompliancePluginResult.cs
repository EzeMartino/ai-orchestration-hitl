using Orchestration.Application.Agents.Legal.AiReview;
using Orchestration.Application.Agents.Legal.Regulations;

namespace Orchestration.Infrastructure.Agents.Legal;

public sealed record LegalCompliancePluginResult(
    bool HasComplianceRisk,
    string RiskLevel,
    string Summary,
    string Engine,
    IReadOnlyList<LegalComplianceEvidenceResult> Evidence,
    IReadOnlyList<string> Warnings,
    object? QueryStrategy = null,
    LegalAnalysisReviewResult? LegalReview = null,
    bool RequiresHumanReview = false,
    RegulatoryEvidenceAssessment? EvidenceAssessment = null,
    IReadOnlyList<RegulatoryEvidenceEnrichment>? EvidenceEnrichments = null
);

public sealed record LegalComplianceEvidenceResult(
    string Regulation,
    string Section,
    string Finding,
    string Source
);
