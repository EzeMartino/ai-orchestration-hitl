namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed class ToolCallingDiagnosticRequest
{
    public IReadOnlyList<ProposedToolCall>? ProposedCalls { get; init; } = [];

    public bool DynamicExecutionEnabled { get; init; } = true;
}
