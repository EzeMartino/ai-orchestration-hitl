namespace Orchestration.Application.Agents.Planner.Reasoning;

public sealed record PlannerReasoningInput(
    Guid SessionId,
    string ReportName,
    decimal TotalAmount,
    int TransactionCount,
    DateTimeOffset SubmittedAt,
    string DataSummary,
    string DataSeverity,
    string DataEngine,
    IReadOnlyList<string> DataEvidence,
    string LegalSummary,
    string LegalRiskLevel,
    string LegalEngine,
    IReadOnlyList<string> LegalEvidence,
    IReadOnlyList<string> LegalWarnings
);
