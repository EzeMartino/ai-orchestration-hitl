namespace Orchestration.Application.Agents.Data;

public sealed record DataAgentResult(
    bool HasAnomaly,
    string Severity,
    string Summary,
    string Engine,
    IReadOnlyList<AnomalyEvidence> Evidence
);
