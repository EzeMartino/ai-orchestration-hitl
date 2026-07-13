using Orchestration.Domain.AnalysisSessions;

namespace Orchestration.Application.Agents.Planner;

public interface IPlannerAgent
{
    Task<PlannerAgentResult> RunAsync(
        Orchestration.Application.Agents.Shared.FinancialReportContext report,
        CancellationToken cancellationToken
    );
}
