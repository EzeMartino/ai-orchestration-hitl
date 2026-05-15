namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed record ToolValidationResult(
    bool IsValid,
    IReadOnlyList<ApprovedToolCall> ApprovedCalls,
    IReadOnlyList<RejectedToolCall> RejectedCalls
);
