using Orchestration.Application.Activity;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Planner.Reasoning;
using Orchestration.Application.Agents.Shared;
using Orchestration.Domain.AnalysisSessions;

namespace Orchestration.Application.Agents.Planner;

public sealed class PlannerAgent : IPlannerAgent
{
    private readonly IDataAgent _dataAgent;
    private readonly ILegalAgent _legalAgent;
    private readonly IActivityEventPublisher _activityPublisher;
    private readonly IPlannerReasoningService _reasoningService;

    public PlannerAgent(
        IDataAgent dataAgent,
        ILegalAgent legalAgent,
        IActivityEventPublisher activityPublisher,
        IPlannerReasoningService reasoningService)
    {
        _dataAgent = dataAgent;
        _legalAgent = legalAgent;
        _activityPublisher = activityPublisher;
        _reasoningService = reasoningService;
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

        var reasoningResult = await _reasoningService.GenerateReasoningAsync(
            BuildReasoningInput(report, dataResult, legalResult),
            cancellationToken
        );

        await PublishAsync(
            session.Id,
            "planner_reasoning_completed",
            "PlannerAgent",
            reasoningResult.Summary,
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
            LegalResult: legalResult,
            ReasoningResult: reasoningResult
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

    private static PlannerReasoningInput BuildReasoningInput(
        FinancialReportContext report,
        DataAgentResult dataResult,
        LegalAgentResult legalResult)
    {
        return new PlannerReasoningInput(
            SessionId: report.SessionId,
            ReportName: report.ReportName,
            TotalAmount: report.TotalAmount,
            TransactionCount: report.TransactionCount,
            DataSummary: dataResult.Summary,
            DataSeverity: dataResult.Severity,
            DataEngine: dataResult.Engine,
            DataEvidence: dataResult.Evidence
                .Select(evidence => $"{evidence.Metric}: value {evidence.Value}, threshold {evidence.Threshold}. {evidence.Interpretation}")
                .ToList(),
            LegalSummary: legalResult.Summary,
            LegalRiskLevel: legalResult.RiskLevel,
            LegalEngine: legalResult.Engine,
            LegalEvidence: legalResult.Evidence
                .Select(evidence => $"{evidence.Regulation} {evidence.Section}: {evidence.Finding} Source: {evidence.Source}")
                .ToList(),
            LegalWarnings: legalResult.Warnings
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
