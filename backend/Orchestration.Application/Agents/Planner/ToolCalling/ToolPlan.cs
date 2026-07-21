namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed record ToolPlan(
    IReadOnlyList<ProposedToolCall> ProposedCalls,
    ToolPlanProposalSource? ProposalSource = null,
    ToolPlanProposalFallbackReason? ProposalFallbackReason = null
);
