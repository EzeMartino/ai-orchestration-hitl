using Orchestration.Application.Activity;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Planner.Reasoning;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.Agents.Planner.ToolCalling.Mapping;
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
    private readonly IControlledToolExecutor _controlledToolExecutor;
    private readonly IToolExecutionResultMapper _executionResultMapper;
    private readonly ToolCallingOptions _toolCallingOptions;

    public PlannerAgent(
        IDataAgent dataAgent,
        ILegalAgent legalAgent,
        IActivityEventPublisher activityPublisher,
        IPlannerReasoningService reasoningService,
        IToolPlanProposalService toolPlanProposalService,
        IToolPlanNormalizer toolPlanNormalizer,
        IToolPlanValidator toolPlanValidator,
        IToolExecutionPolicy toolExecutionPolicy,
        IControlledToolExecutor controlledToolExecutor,
        IToolExecutionResultMapper executionResultMapper,
        ToolCallingOptions toolCallingOptions)
    {
        _dataAgent = dataAgent;
        _legalAgent = legalAgent;
        _activityPublisher = activityPublisher;
        _reasoningService = reasoningService;
        _toolPlanProposalService = toolPlanProposalService;
        _toolPlanNormalizer = toolPlanNormalizer;
        _toolPlanValidator = toolPlanValidator;
        _toolExecutionPolicy = toolExecutionPolicy;
        _controlledToolExecutor = controlledToolExecutor;
        _executionResultMapper = executionResultMapper;
        _toolCallingOptions = toolCallingOptions;
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

        if (IsPlanDrivenMode())
        {
            return await RunPlanDrivenModeAsync(
                session.Id,
                report,
                cancellationToken
            );
        }

        return await RunShadowModeAsync(
            session.Id,
            report,
            cancellationToken
        );
    }

    private bool IsPlanDrivenMode()
    {
        return _toolCallingOptions.Enabled &&
            _toolCallingOptions.ExecutionMode == ToolCallingExecutionMode.PlanDriven;
    }

    private async Task<PlannerAgentResult> RunShadowModeAsync(
        Guid sessionId,
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        var (dataResult, legalResult) = await RunDeterministicAgentsAsync(
            sessionId,
            report,
            cancellationToken
        );

        var reasoningResult = await GenerateReasoningAsync(
            sessionId,
            report,
            dataResult,
            legalResult,
            cancellationToken
        );

        var proposedPlan = await _toolPlanProposalService.ProposeAsync(
            BuildToolPlanProposalInput(report, reasoningResult),
            cancellationToken
        );

        var toolPlan = await BuildToolPlanAuditAsync(
            proposedPlan,
            new ToolExecutionPolicyContext(
                DataAnalysisAlreadyCompleted: true,
                LegalReviewAlreadyCompleted: true,
                DynamicExecutionEnabled: false
            ),
            cancellationToken
        );

        await PublishToolPlanAuditEventsAsync(
            sessionId,
            toolPlan,
            cancellationToken
        );

        return await CompletePlannerAsync(
            sessionId,
            dataResult,
            legalResult,
            reasoningResult,
            toolPlan,
            cancellationToken
        );
    }

    private async Task<PlannerAgentResult> RunPlanDrivenModeAsync(
        Guid sessionId,
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        var proposedPlan = await _toolPlanProposalService.ProposeAsync(
            BuildInitialToolPlanProposalInput(report),
            cancellationToken
        );

        var toolPlan = await BuildToolPlanAuditAsync(
            proposedPlan,
            new ToolExecutionPolicyContext(
                DataAnalysisAlreadyCompleted: false,
                LegalReviewAlreadyCompleted: false,
                DynamicExecutionEnabled: true
            ),
            cancellationToken
        );

        await PublishToolPlanAuditEventsAsync(
            sessionId,
            toolPlan,
            cancellationToken
        );

        var dataResult = _executionResultMapper.TryMapDataResult(toolPlan.ExecutedCalls);
        var legalResult = _executionResultMapper.TryMapLegalResult(toolPlan.ExecutedCalls);

        if (dataResult is null || legalResult is null)
        {
            await PublishAsync(
                sessionId,
                "tool_execution_fallback_used",
                "PlannerAgent",
                "Plan-driven execution failed; deterministic agent path was used.",
                cancellationToken
            );

            (dataResult, legalResult) = await RunDeterministicAgentsAsync(
                sessionId,
                report,
                cancellationToken
            );
        }

        var reasoningResult = await GenerateReasoningAsync(
            sessionId,
            report,
            dataResult,
            legalResult,
            cancellationToken
        );

        return await CompletePlannerAsync(
            sessionId,
            dataResult,
            legalResult,
            reasoningResult,
            toolPlan,
            cancellationToken
        );
    }

    private async Task<(DataAgentResult DataResult, LegalAgentResult LegalResult)> RunDeterministicAgentsAsync(
        Guid sessionId,
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        await PublishAsync(
            sessionId,
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
            sessionId,
            "tool_executed",
            "DataAgent",
            $"Anomaly detection completed using {dataResult.Engine}.",
            cancellationToken
        );

        await PublishAsync(
            sessionId,
            "agent_completed",
            "DataAgent",
            dataResult.Summary,
            cancellationToken
        );

        await PublishAsync(
            sessionId,
            "agent_task_delegated",
            "PlannerAgent",
            "Delegating compliance review to LegalAgent.",
            cancellationToken
        );

        var legalReport = dataResult.FinancialAnalysis is null
            ? report
            : report with { FinancialAnalysis = dataResult.FinancialAnalysis };

        var legalResult = await _legalAgent.ReviewAsync(
            legalReport,
            cancellationToken
        );

        await PublishAsync(
            sessionId,
            "tool_executed",
            "LegalAgent",
            $"Compliance review completed using {legalResult.Engine}.",
            cancellationToken
        );

        await PublishAsync(
            sessionId,
            "agent_completed",
            "LegalAgent",
            legalResult.Summary,
            cancellationToken
        );

        return (dataResult, legalResult);
    }

    private async Task<PlannerReasoningResult> GenerateReasoningAsync(
        Guid sessionId,
        FinancialReportContext report,
        DataAgentResult dataResult,
        LegalAgentResult legalResult,
        CancellationToken cancellationToken)
    {
        var reasoningResult = await _reasoningService.GenerateReasoningAsync(
            BuildReasoningInput(report, dataResult, legalResult),
            cancellationToken
        );

        await PublishAsync(
            sessionId,
            GetReasoningEventType(reasoningResult),
            "PlannerAgent",
            GetReasoningEventMessage(reasoningResult),
            cancellationToken
        );

        return reasoningResult;
    }

    private async Task<ToolPlanAuditResult> BuildToolPlanAuditAsync(
        ToolPlan proposedPlan,
        ToolExecutionPolicyContext policyContext,
        CancellationToken cancellationToken)
    {
        var normalizedPlan = _toolPlanNormalizer.Normalize(proposedPlan);
        var validationResult = _toolPlanValidator.Validate(normalizedPlan);
        var policyDecisions = _toolExecutionPolicy.Decide(
            validationResult.ApprovedCalls,
            policyContext
        );
        var executionAudit = await BuildExecutionAuditAsync(
            policyDecisions,
            cancellationToken
        );

        return new ToolPlanAuditResult(
            ProposedCalls: normalizedPlan.ProposedCalls,
            ApprovedCalls: validationResult.ApprovedCalls,
            RejectedCalls: validationResult.RejectedCalls,
            ExecutedCalls: executionAudit
        );
    }

    private async Task<PlannerAgentResult> CompletePlannerAsync(
        Guid sessionId,
        DataAgentResult dataResult,
        LegalAgentResult legalResult,
        PlannerReasoningResult reasoningResult,
        ToolPlanAuditResult toolPlan,
        CancellationToken cancellationToken)
    {
        var requiresHumanApproval =
            dataResult.HasAnomaly ||
            legalResult.HasComplianceRisk;

        var summary = requiresHumanApproval
            ? "PlannerAgent determined that human approval is required before completing the workflow."
            : "PlannerAgent determined that the workflow can be completed without human intervention.";

        await PublishAsync(
            sessionId,
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

    private async Task<IReadOnlyList<ToolExecutionResult>> BuildExecutionAuditAsync(
        IReadOnlyList<ToolExecutionPolicyDecision> policyDecisions,
        CancellationToken cancellationToken)
    {
        var callsToExecute = policyDecisions
            .Where(decision => decision.Status == ToolExecutionStatus.Executed)
            .Select(decision => decision.Call)
            .ToList();
        IReadOnlyList<ToolExecutionResult> executionResults;

        try
        {
            executionResults = callsToExecute.Count == 0
                ? []
                : await _controlledToolExecutor.ExecuteAsync(callsToExecute, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            executionResults = callsToExecute
                .Select(call => new ToolExecutionResult(
                    ToolName: call.ToolName,
                    Status: ToolExecutionStatus.Failed,
                    Succeeded: false,
                    Summary: "Tool execution failed.",
                    Engine: "Controlled Tool Executor",
                    OutputJson: "{}",
                    Error: "Tool execution failed."
                ))
                .ToList();
        }

        var executionIndex = 0;
        var executedCalls = new List<ToolExecutionResult>();

        foreach (var decision in policyDecisions)
        {
            if (decision.Status == ToolExecutionStatus.Executed)
            {
                executedCalls.Add(
                    executionIndex < executionResults.Count
                        ? executionResults[executionIndex]
                        : CreateMissingExecutionResult(decision.Call)
                );
                executionIndex++;
                continue;
            }

            executedCalls.Add(CreatePolicyAuditResult(decision));
        }

        return executedCalls;
    }

    private static ToolExecutionResult CreateMissingExecutionResult(
        ApprovedToolCall call)
    {
        return new ToolExecutionResult(
            ToolName: call.ToolName,
            Status: ToolExecutionStatus.Failed,
            Succeeded: false,
            Summary: "Tool execution result was not returned.",
            Engine: "Controlled Tool Executor",
            OutputJson: "{}",
            Error: "Tool execution result was not returned."
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

            if (executionAudit.Status == ToolExecutionStatus.Executed)
            {
                await PublishAsync(
                    sessionId,
                    "tool_call_executed",
                    "PlannerAgent",
                    $"Executed approved tool call '{executionAudit.ToolName}' using {executionAudit.Engine}.",
                    cancellationToken
                );
            }

            if (executionAudit.Status == ToolExecutionStatus.Failed)
            {
                await PublishAsync(
                    sessionId,
                    "tool_call_failed",
                    "PlannerAgent",
                    $"Approved tool call '{executionAudit.ToolName}' failed: {executionAudit.Error ?? executionAudit.Summary}",
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

    private static ToolPlanProposalInput BuildInitialToolPlanProposalInput(
        FinancialReportContext report)
    {
        return new ToolPlanProposalInput(
            SessionId: report.SessionId,
            ReportName: report.ReportName,
            TotalAmount: report.TotalAmount,
            TransactionCount: report.TransactionCount,
            PlannerSummary: "Collect read-only financial anomaly and regulatory retrieval evidence before planner reasoning.",
            RiskFactors: [],
            Limitations:
            [
                "The LLM may propose tools only; workflow control remains deterministic.",
                "Human approval remains mandatory when risk exists."
            ]
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
