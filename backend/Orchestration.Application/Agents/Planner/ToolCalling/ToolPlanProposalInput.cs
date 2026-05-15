namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed record ToolPlanProposalInput(
    Guid SessionId,
    string ReportName,
    decimal TotalAmount,
    int TransactionCount,
    string PlannerSummary,
    IReadOnlyList<string> RiskFactors,
    IReadOnlyList<string> Limitations
);
