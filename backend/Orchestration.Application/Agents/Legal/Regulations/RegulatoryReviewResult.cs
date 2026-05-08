namespace Orchestration.Application.Agents.Legal.Regulations;

public sealed record RegulatoryReviewResult(
    bool HasComplianceRisk,
    string RiskLevel,
    string Summary,
    string SourceEngine,
    IReadOnlyList<RegulatoryFinding> Findings
);