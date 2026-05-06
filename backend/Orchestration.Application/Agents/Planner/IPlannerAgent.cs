using Orchestration.Domain.AnalysisSessions;

namespace Orchestration.Application.Agents.Planner;

public interface IPlannerAgent
{
    Task<PlannerAgentResult> RunAsync(
        AnalysisSession session,
        CancellationToken cancellationToken
    );
}