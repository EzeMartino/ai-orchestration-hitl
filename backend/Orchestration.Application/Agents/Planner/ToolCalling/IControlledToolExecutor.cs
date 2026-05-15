namespace Orchestration.Application.Agents.Planner.ToolCalling;

public interface IControlledToolExecutor
{
    Task<IReadOnlyList<ToolExecutionResult>> ExecuteAsync(
        IReadOnlyList<ApprovedToolCall> calls,
        CancellationToken cancellationToken);
}
