namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed record ToolPlanAuditResult(
    IReadOnlyList<ProposedToolCall> ProposedCalls,
    IReadOnlyList<ApprovedToolCall> ApprovedCalls,
    IReadOnlyList<RejectedToolCall> RejectedCalls,
    IReadOnlyList<ToolExecutionResult> ExecutedCalls,
    ToolPlanProposalSource? ProposalSource = null,
    ToolPlanProposalFallbackReason? ProposalFallbackReason = null
)
{
    public static ToolPlanAuditResult Empty { get; } = new([], [], [], []);
}
