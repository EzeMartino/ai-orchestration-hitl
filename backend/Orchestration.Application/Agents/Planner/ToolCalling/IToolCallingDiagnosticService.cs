namespace Orchestration.Application.Agents.Planner.ToolCalling;

public interface IToolCallingDiagnosticService
{
    Task<ToolPlanAuditResult> ExecuteAsync(
        ToolCallingDiagnosticRequest request,
        CancellationToken cancellationToken);
}
