namespace Orchestration.Infrastructure.Agents.Legal;

public sealed record LegalCompliancePluginResult(
    bool HasComplianceRisk,
    string RiskLevel,
    string Summary,
    string Engine,
    IReadOnlyList<LegalComplianceEvidenceResult> Evidence,
    IReadOnlyList<string> Warnings,
    object? QueryStrategy = null
);

public sealed record LegalComplianceEvidenceResult(
    string Regulation,
    string Section,
    string Finding,
    string Source
);
