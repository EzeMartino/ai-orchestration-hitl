namespace Orchestration.Application.Agents.Data;

public sealed record DataAgentResult(
    bool HasAnomaly,
    string Severity,
    string Summary,
    IReadOnlyList<AnomalyEvidence> Evidence
);