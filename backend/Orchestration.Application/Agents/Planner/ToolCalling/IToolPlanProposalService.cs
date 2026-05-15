namespace Orchestration.Application.Agents.Planner.ToolCalling;

public interface IToolPlanProposalService
{
    Task<ToolPlan> ProposeAsync(
        ToolPlanProposalInput input,
        CancellationToken cancellationToken);
}
