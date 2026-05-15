namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed record RejectedToolCall(
    string ToolName,
    string Reason
);
