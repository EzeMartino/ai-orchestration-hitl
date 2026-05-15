namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed record ToolExecutionPolicyDecision(
    ApprovedToolCall Call,
    ToolExecutionStatus Status,
    string Reason
);
