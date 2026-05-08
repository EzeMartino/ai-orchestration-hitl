namespace Orchestration.Infrastructure.Agents.Data;

public sealed record PythonAnomalyDetectionPluginResult(
    bool HasAnomaly,
    string Severity,
    string Summary,
    string Engine,
    IReadOnlyList<PythonAnomalyEvidenceResult> Evidence
);

public sealed record PythonAnomalyEvidenceResult(
    string Metric,
    double Value,
    double Threshold,
    string Interpretation
);