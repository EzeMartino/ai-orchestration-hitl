using Orchestration.Application.Activity;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Shared;
using Orchestration.Domain.AnalysisSessions;

namespace Orchestration.Application.Agents.Planner;

public sealed class PlannerAgent : IPlannerAgent
{
    private readonly IDataAgent _dataAgent;
    private readonly ILegalAgent _legalAgent;
    private readonly IActivityEventPublisher _activityPublisher;

    public PlannerAgent(
        IDataAgent dataAgent,
        ILegalAgent legalAgent,
        IActivityEventPublisher activityPublisher)
    {
        _dataAgent = dataAgent;
        _legalAgent = legalAgent;
        _activityPublisher = activityPublisher;
    }

    public async Task<PlannerAgentResult> RunAsync(
        AnalysisSession session,
        CancellationToken cancellationToken)
    {
        await PublishAsync(
            session.Id,
            "agent_started",
            "PlannerAgent",
            "PlannerAgent initialized. Building execution plan.",
            cancellationToken
        );

        var report = BuildReportContext(session);

        await PublishAsync(
            session.Id,
            "agent_task_delegated",
            "PlannerAgent",
            "Delegating anomaly detection to DataAgent.",
            cancellationToken
        );

        var dataResult = await _dataAgent.AnalyzeAsync(
            report,
            cancellationToken
        );

        await PublishAsync(
            session.Id,
            "tool_executed",
            "DataAgent",
            $"Anomaly detection completed using {dataResult.Engine}.",
            cancellationToken
        );

        await PublishAsync(
            session.Id,
            "agent_completed",
            "DataAgent",
            dataResult.Summary,
            cancellationToken
        );

        await PublishAsync(
            session.Id,
            "agent_task_delegated",
            "PlannerAgent",
            "Delegating compliance review to LegalAgent.",
            cancellationToken
        );

        var legalResult = await _legalAgent.ReviewAsync(
            report,
            cancellationToken
        );

        await PublishAsync(
            session.Id,
            "tool_executed",
            "LegalAgent",
            $"Compliance review completed using {legalResult.Engine}.",
            cancellationToken
        );

        await PublishAsync(
            session.Id,
            "agent_completed",
            "LegalAgent",
            legalResult.Summary,
            cancellationToken
        );

        var requiresHumanApproval =
            dataResult.HasAnomaly ||
            legalResult.HasComplianceRisk;

        var summary = requiresHumanApproval
            ? "PlannerAgent determined that human approval is required before completing the workflow."
            : "PlannerAgent determined that the workflow can be completed without human intervention.";

        await PublishAsync(
            session.Id,
            "agent_completed",
            "PlannerAgent",
            summary,
            cancellationToken
        );

        return new PlannerAgentResult(
            RequiresHumanApproval: requiresHumanApproval,
            Summary: summary,
            DataResult: dataResult,
            LegalResult: legalResult
        );
    }

    private static FinancialReportContext BuildReportContext(
        AnalysisSession session)
    {
        return new FinancialReportContext(
            SessionId: session.Id,
            ReportName: $"financial-report-{session.Id}",
            TotalAmount: 125000m,
            TransactionCount: 42,
            SubmittedAt: session.CreatedAt
        );
    }

    private async Task PublishAsync(
        Guid sessionId,
        string type,
        string agent,
        string message,
        CancellationToken cancellationToken)
    {
        await _activityPublisher.PublishAsync(
            new ActivityEvent(
                sessionId,
                type,
                agent,
                message,
                DateTimeOffset.UtcNow
            ),
            cancellationToken
        );
    }
}
