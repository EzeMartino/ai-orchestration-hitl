namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed record ToolPlanProposalInput(
    Guid SessionId,
    string ReportName,
    decimal TotalAmount,
    int TransactionCount,
    DateTimeOffset SubmittedAt,
    string PlannerSummary,
    IReadOnlyList<string> RiskFactors,
    IReadOnlyList<string> Limitations
);
