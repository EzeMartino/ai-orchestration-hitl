namespace Orchestration.Application.Agents.Planner.ToolCalling;

public interface IToolPlanNormalizer
{
    ToolPlan Normalize(
        ToolPlan plan);
}
