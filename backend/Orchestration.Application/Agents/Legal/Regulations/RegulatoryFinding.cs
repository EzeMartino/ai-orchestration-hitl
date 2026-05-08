namespace Orchestration.Application.Agents.Legal.Regulations;

public sealed record RegulatoryFinding(
    string Regulation,
    string Section,
    string Finding,
    string Source
);