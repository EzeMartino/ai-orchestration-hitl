namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed record ApprovedToolCall(
    string ToolName,
    IReadOnlyDictionary<string, string> Arguments,
    string Reason
);
