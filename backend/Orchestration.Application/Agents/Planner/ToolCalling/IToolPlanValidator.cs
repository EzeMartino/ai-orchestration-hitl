namespace Orchestration.Application.Agents.Planner.ToolCalling;

public interface IToolPlanValidator
{
    ToolValidationResult Validate(ToolPlan plan);
}
