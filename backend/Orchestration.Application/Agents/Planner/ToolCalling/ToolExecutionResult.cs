namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed record ToolExecutionResult(
    string ToolName,
    ToolExecutionStatus Status,
    bool Succeeded,
    string Summary,
    string Engine,
    string OutputJson,
    string? Error
);
