namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed record ProposedToolCall(
    string ToolName,
    IReadOnlyDictionary<string, string> Arguments,
    string Reason
);
