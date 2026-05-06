namespace Orchestration.Application.Agents.Data;

public sealed record AnomalyEvidence(
    string Metric,
    double Value,
    double Threshold,
    string Interpretation
);