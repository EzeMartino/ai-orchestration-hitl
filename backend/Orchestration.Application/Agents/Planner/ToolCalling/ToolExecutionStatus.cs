namespace Orchestration.Application.Agents.Planner.ToolCalling;

public enum ToolExecutionStatus
{
    Executed,
    SkippedAlreadySatisfied,
    SkippedDisabled,
    Failed
}
