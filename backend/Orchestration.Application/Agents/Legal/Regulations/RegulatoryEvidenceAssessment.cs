namespace Orchestration.Application.Agents.Legal.Regulations;

public sealed record RegulatoryEvidenceAssessment(
    bool EvidenceFound,
    string Relevance,
    string Applicability,
    string EvidenceQuality,
    string Severity,
    bool RequiresHumanReview,
    IReadOnlyList<string> Reasons);
