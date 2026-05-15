using Orchestration.Application.Activity;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Planner.Reasoning;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.Agents.Shared;
using Orchestration.Domain.AnalysisSessions;

namespace Orchestration.Application.Agents.Planner;

public sealed class PlannerAgent : IPlannerAgent
{
    private readonly IDataAgent _dataAgent;
    private readonly ILegalAgent _legalAgent;
    private readonly IActivityEventPublisher _activityPublisher;
    private readonly IPlannerReasoningService _reasoningService;
    private readonly IToolPlanProposalService _toolPlanProposalService;
    private readonly IToolPlanNormalizer _toolPlanNormalizer;
    private readonly IToolPlanValidator _toolPlanValidator;
    private readonly IToolExecutionPolicy _toolExecutionPolicy;

    public PlannerAgent(
        IDataAgent dataAgent,
        ILegalAgent legalAgent,
        IActivityEventPublisher activityPublisher,
        IPlannerReasoningService reasoningService,
        IToolPlanProposalService toolPlanProposalService,
        IToolPlanNormalizer toolPlanNormalizer,
        IToolPlanValidator toolPlanValidator,
        IToolExecutionPolicy toolExecutionPolicy)
    {
        _dataAgent = dataAgent;
        _legalAgent = legalAgent;
        _activityPublisher = activityPublisher;
        _reasoningService = reasoningService;
        _toolPlanProposalService = toolPlanProposalService;
        _toolPlanNormalizer = toolPlanNormalizer;
        _toolPlanValidator = toolPlanValidator;
        _toolExecutionPolicy = toolExecutionPolicy;
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
            GetReasoningEventType(reasoningResult),
            "PlannerAgent",
            GetReasoningEventMessage(reasoningResult),
            cancellationToken
        );

        var proposedPlan = await _toolPlanProposalService.ProposeAsync(
            BuildToolPlanProposalInput(report, reasoningResult),
            cancellationToken
        );

        var normalizedPlan = _toolPlanNormalizer.Normalize(proposedPlan);
        var validationResult = _toolPlanValidator.Validate(normalizedPlan);
        var policyDecisions = _toolExecutionPolicy.Decide(
            validationResult.ApprovedCalls,
            new ToolExecutionPolicyContext(
                DataAnalysisAlreadyCompleted: true,
                LegalReviewAlreadyCompleted: true,
                DynamicExecutionEnabled: false
            )
        );
        var executionAudit = policyDecisions
            .Select(CreatePolicyAuditResult)
            .ToList();

        var toolPlan = new ToolPlanAuditResult(
            ProposedCalls: normalizedPlan.ProposedCalls,
            ApprovedCalls: validationResult.ApprovedCalls,
            RejectedCalls: validationResult.RejectedCalls,
            ExecutedCalls: executionAudit
        );

        await PublishToolPlanAuditEventsAsync(
            session.Id,
            toolPlan,
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
            ReasoningResult: reasoningResult,
            ToolPlan: toolPlan
        );
    }

    private async Task PublishToolPlanAuditEventsAsync(
        Guid sessionId,
        ToolPlanAuditResult toolPlan,
        CancellationToken cancellationToken)
    {
        if (toolPlan.ProposedCalls.Count == 0 &&
            toolPlan.ApprovedCalls.Count == 0 &&
            toolPlan.RejectedCalls.Count == 0 &&
            toolPlan.ExecutedCalls.Count == 0)
        {
            return;
        }

        if (toolPlan.ProposedCalls.Count > 0)
        {
            await PublishAsync(
                sessionId,
                "tool_plan_proposed",
                "PlannerAgent",
                $"PlannerAgent proposed {toolPlan.ProposedCalls.Count} tool calls.",
                cancellationToken
            );
        }

        if (toolPlan.ApprovedCalls.Count > 0 || toolPlan.RejectedCalls.Count > 0)
        {
            await PublishAsync(
                sessionId,
                "tool_plan_validated",
                "PlannerAgent",
                $"Tool plan validated: {toolPlan.ApprovedCalls.Count} approved, {toolPlan.RejectedCalls.Count} rejected.",
                cancellationToken
            );
        }

        foreach (var rejectedCall in toolPlan.RejectedCalls)
        {
            await PublishAsync(
                sessionId,
                "tool_call_rejected",
                "PlannerAgent",
                $"Rejected tool call '{rejectedCall.ToolName}': {rejectedCall.Reason}",
                cancellationToken
            );
        }

        foreach (var executionAudit in toolPlan.ExecutedCalls)
        {
            if (executionAudit.Status is ToolExecutionStatus.SkippedAlreadySatisfied or ToolExecutionStatus.SkippedDisabled)
            {
                await PublishAsync(
                    sessionId,
                    "tool_call_skipped",
                    "PlannerAgent",
                    $"Skipped approved tool call '{executionAudit.ToolName}': {executionAudit.Summary}",
                    cancellationToken
                );
            }
        }
    }

    private static ToolExecutionResult CreatePolicyAuditResult(
        ToolExecutionPolicyDecision decision)
    {
        var failed = decision.Status == ToolExecutionStatus.Failed;

        return new ToolExecutionResult(
            ToolName: decision.Call.ToolName,
            Status: decision.Status,
            Succeeded: !failed,
            Summary: decision.Reason,
            Engine: "Tool Execution Policy",
            OutputJson: "{}",
            Error: failed ? decision.Reason : null
        );
    }

    private static string GetReasoningEventType(
        PlannerReasoningResult reasoningResult)
    {
        return reasoningResult.UsedFallback && !string.IsNullOrWhiteSpace(reasoningResult.FailureReason)
            ? "planner_reasoning_fallback_used"
            : "planner_reasoning_completed";
    }

    private static string GetReasoningEventMessage(
        PlannerReasoningResult reasoningResult)
    {
        if (reasoningResult.UsedFallback && !string.IsNullOrWhiteSpace(reasoningResult.FailureReason))
        {
            return "LLM reasoning failed; deterministic fallback was used.";
        }

        if (reasoningResult.UsedLlm)
        {
            return $"Planner reasoning completed using {reasoningResult.Engine}.";
        }

        return "Planner reasoning completed using deterministic fallback.";
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

    private static ToolPlanProposalInput BuildToolPlanProposalInput(
        FinancialReportContext report,
        PlannerReasoningResult reasoningResult)
    {
        return new ToolPlanProposalInput(
            SessionId: report.SessionId,
            ReportName: report.ReportName,
            TotalAmount: report.TotalAmount,
            TransactionCount: report.TransactionCount,
            PlannerSummary: reasoningResult.Summary,
            RiskFactors: reasoningResult.RiskFactors,
            Limitations: reasoningResult.Limitations
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
