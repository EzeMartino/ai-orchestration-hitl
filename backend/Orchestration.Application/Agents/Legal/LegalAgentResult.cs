namespace Orchestration.Application.Agents.Legal;

public sealed record LegalAgentResult(
    bool HasComplianceRisk,
    string RiskLevel,
    string Summary,
    IReadOnlyList<LegalEvidence> Evidence
);