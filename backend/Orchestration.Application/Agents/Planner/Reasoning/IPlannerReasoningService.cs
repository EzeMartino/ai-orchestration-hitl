namespace Orchestration.Application.Agents.Planner.Reasoning;

public interface IPlannerReasoningService
{
    Task<PlannerReasoningResult> GenerateReasoningAsync(
        PlannerReasoningInput input,
        CancellationToken cancellationToken);
}
