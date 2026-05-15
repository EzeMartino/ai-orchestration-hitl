namespace Orchestration.Application.Agents.Planner.ToolCalling;

public interface IToolExecutionPolicy
{
    IReadOnlyList<ToolExecutionPolicyDecision> Decide(
        IReadOnlyList<ApprovedToolCall> approvedCalls,
        ToolExecutionPolicyContext context);
}
