namespace Orchestration.Application.Agents.Legal;

public sealed record LegalEvidence(
    string Regulation,
    string Section,
    string Finding,
    string Source
);