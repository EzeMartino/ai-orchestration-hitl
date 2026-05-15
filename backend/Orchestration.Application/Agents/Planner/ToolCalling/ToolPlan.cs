namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed record ToolPlan(
    IReadOnlyList<ProposedToolCall> ProposedCalls
);
