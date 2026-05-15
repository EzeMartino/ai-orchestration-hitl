namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed record ToolExecutionResult(
    string ToolName,
    bool Succeeded,
    string Summary,
    string Engine,
    string OutputJson,
    string? Error
);
