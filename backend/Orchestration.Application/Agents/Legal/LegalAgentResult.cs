namespace Orchestration.Application.Agents.Legal;

public sealed record LegalAgentResult(
    bool HasComplianceRisk,
    string RiskLevel,
    string Summary,
    string Engine,
    IReadOnlyList<LegalEvidence> Evidence,
    IReadOnlyList<string> Warnings
);
