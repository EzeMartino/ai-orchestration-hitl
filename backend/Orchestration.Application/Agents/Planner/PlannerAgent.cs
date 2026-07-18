using Orchestration.Application.Activity;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Legal.Cnv;
using Orchestration.Application.Agents.Planner.Reasoning;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.Agents.Planner.ToolCalling.Mapping;
using Orchestration.Application.Agents.Shared;
using Orchestration.Domain.AnalysisSessions;

namespace Orchestration.Application.Agents.Planner;

public sealed class PlannerAgent : IPlannerAgent
{
    private const string UnsupportedSatisfactionKindReason =
        "Unsupported planner satisfaction kind.";
    private const string PolicyDecisionMismatchReason =
        "Tool execution policy returned decisions inconsistent with validation.";
    private const string MissingExecutionResultReason =
        "Controlled tool executor returned no matching result.";
    private const string UnexpectedExecutionResultsReason =
        "Controlled tool executor returned unexpected results.";

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
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        await PublishAsync(
            report.SessionId,
            "agent_started",
            "PlannerAgent",
            "PlannerAgent (Agente Planificador) inicializado. Construyendo plan de ejecución.",
            cancellationToken
        );
        cancellationToken.ThrowIfCancellationRequested();

        if (IsPlanDrivenMode())
        {
            return await RunPlanDrivenModeAsync(
                report.SessionId,
                report,
                cancellationToken
            );
        }

        return await RunShadowModeAsync(
            report.SessionId,
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
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedPlan = _toolPlanNormalizer.Normalize(proposedPlan);
        var validationResult = _toolPlanValidator.Validate(normalizedPlan);
        var returnedPolicyDecisions = _toolExecutionPolicy.Decide(
            validationResult.ApprovedCalls,
            new ToolExecutionPolicyContext(
                DataAnalysisAlreadyCompleted: false,
                LegalReviewAlreadyCompleted: false,
                DynamicExecutionEnabled: true
            )
        );
        var policyDecisions = ReconcilePolicyDecisions(
            validationResult.ApprovedCalls,
            returnedPolicyDecisions);
        var planBeforeExecution = new ToolPlanAuditResult(
            ProposedCalls: normalizedPlan.ProposedCalls,
            ApprovedCalls: validationResult.ApprovedCalls,
            RejectedCalls: validationResult.RejectedCalls,
            ExecutedCalls: [],
            ProposalSource: normalizedPlan.ProposalSource,
            ProposalFallbackReason: normalizedPlan.ProposalFallbackReason
        );

        await PublishToolPlanValidationEventsAsync(
            sessionId,
            planBeforeExecution,
            cancellationToken
        );
        cancellationToken.ThrowIfCancellationRequested();

        var decisionPartitions = PartitionPolicyDecisions(policyDecisions);
        var dataDecisions = decisionPartitions.Data;
        var legalDecisions = decisionPartitions.Legal;

        var dataAudit = await BuildExecutionAuditAsync(
            dataDecisions,
            cancellationToken,
            runtimeContext: null
        );
        await PublishToolExecutionAuditEventsAsync(
            sessionId,
            dataAudit,
            cancellationToken
        );
        cancellationToken.ThrowIfCancellationRequested();

        var dataResult = TryMapExecutionResult(
                () => _executionResultMapper.TryMapDataResult(dataAudit),
                cancellationToken) ??
            PlannerStageSafeResults.DataUnavailable();
        var dataToolStatus = GetDataToolStatus(dataDecisions, dataAudit);
        var dataEvidence = LegalDataEvidenceClassifier.Classify(
            dataToolStatus,
            dataResult.FinancialAnalysis);
        var runtimeContext = new PlannerToolExecutionContext(
            report,
            dataResult,
            dataEvidence);

        var legalAudit = await BuildExecutionAuditAsync(
            legalDecisions,
            cancellationToken,
            runtimeContext
        );
        await PublishToolExecutionAuditEventsAsync(
            sessionId,
            legalAudit,
            cancellationToken
        );
        cancellationToken.ThrowIfCancellationRequested();

        // Unsupported decisions are never executable stages. Record them only
        // after the actual Data -> Legal execution order has been audited.
        var unsupportedAudit = CreateUnsupportedExecutionAudit(
            decisionPartitions.Unsupported);
        await PublishToolExecutionAuditEventsAsync(
            sessionId,
            unsupportedAudit,
            cancellationToken
        );
        cancellationToken.ThrowIfCancellationRequested();

        var legalResult = TryMapExecutionResult(
                () => _executionResultMapper.TryMapLegalResult(legalAudit),
                cancellationToken) ??
            PlannerStageSafeResults.LegalUnavailable();
        var toolPlan = planBeforeExecution with
        {
            ExecutedCalls = dataAudit
                .Concat(legalAudit)
                .Concat(unsupportedAudit)
                .ToArray()
        };

        cancellationToken.ThrowIfCancellationRequested();
        var reasoningResult = await GenerateReasoningAsync(
            sessionId,
            report,
            dataResult,
            legalResult,
            cancellationToken
        );

        cancellationToken.ThrowIfCancellationRequested();
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
            "Delegando detección de anomalías a DataAgent (Agente de Datos).",
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
            $"Detección de anomalías completada usando {dataResult.Engine}.",
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
            "Delegando revisión de cumplimiento normativo a LegalAgent (Agente Legal).",
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
            $"Revisión de cumplimiento completada usando {legalResult.Engine}.",
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
        cancellationToken.ThrowIfCancellationRequested();

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
        var returnedPolicyDecisions = _toolExecutionPolicy.Decide(
            validationResult.ApprovedCalls,
            policyContext
        );
        var policyDecisions = ReconcilePolicyDecisions(
            validationResult.ApprovedCalls,
            returnedPolicyDecisions);
        var executionAudit = await BuildExecutionAuditAsync(
            policyDecisions,
            cancellationToken
        );

        return new ToolPlanAuditResult(
            ProposedCalls: normalizedPlan.ProposedCalls,
            ApprovedCalls: validationResult.ApprovedCalls,
            RejectedCalls: validationResult.RejectedCalls,
            ExecutedCalls: executionAudit,
            ProposalSource: normalizedPlan.ProposalSource,
            ProposalFallbackReason: normalizedPlan.ProposalFallbackReason
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
            dataResult.RequiresHumanReview ||
            legalResult.HasComplianceRisk ||
            legalResult.RequiresHumanReview;

        var summary = requiresHumanApproval
            ? "PlannerAgent determinó que se requiere aprobación humana antes de completar el flujo de trabajo."
            : "PlannerAgent determinó que el flujo de trabajo puede completarse sin intervención humana.";

        cancellationToken.ThrowIfCancellationRequested();
        await PublishAsync(
            sessionId,
            "agent_completed",
            "PlannerAgent",
            summary,
            cancellationToken
        );
        cancellationToken.ThrowIfCancellationRequested();

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
        CancellationToken cancellationToken,
        PlannerToolExecutionContext? runtimeContext = null)
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
                : await _controlledToolExecutor.ExecuteAsync(
                    callsToExecute,
                    cancellationToken,
                    runtimeContext);
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
                    Summary: "La ejecución de la herramienta falló.",
                    Engine: "Controlled Tool Executor",
                    OutputJson: "{}",
                    Error: "La ejecución de la herramienta falló."
                ))
                .ToList();
        }

        var reconciledExecutionResults = ReconcileExecutionResults(
            callsToExecute,
            executionResults);
        var executionIndex = 0;
        var executedCalls = new List<ToolExecutionResult>();

        foreach (var decision in policyDecisions)
        {
            if (decision.Status == ToolExecutionStatus.Executed)
            {
                executedCalls.Add(
                    reconciledExecutionResults[executionIndex]
                );
                executionIndex++;
                continue;
            }

            executedCalls.Add(CreatePolicyAuditResult(decision));
        }

        return executedCalls;
    }

    internal static IReadOnlyList<ToolExecutionPolicyDecision> ReconcilePolicyDecisions(
        IReadOnlyList<ApprovedToolCall> approvedCalls,
        IReadOnlyList<ToolExecutionPolicyDecision>? policyDecisions)
    {
        if (policyDecisions is not null &&
            policyDecisions.Count == approvedCalls.Count)
        {
            var unmatchedApprovedCalls = approvedCalls.ToList();
            var isExactMatch = true;

            foreach (var decision in policyDecisions)
            {
                if (decision is null ||
                    decision.Call is null ||
                    !IsAllowedPolicyStatus(decision.Status))
                {
                    isExactMatch = false;
                    break;
                }

                var matchedIndex = unmatchedApprovedCalls.FindIndex(call =>
                    ReferenceEquals(call, decision.Call));
                if (matchedIndex < 0)
                {
                    isExactMatch = false;
                    break;
                }

                unmatchedApprovedCalls.RemoveAt(matchedIndex);
            }

            if (isExactMatch && unmatchedApprovedCalls.Count == 0)
            {
                return policyDecisions;
            }
        }

        return approvedCalls
            .Select(call => new ToolExecutionPolicyDecision(
                call,
                ToolExecutionStatus.Failed,
                PolicyDecisionMismatchReason))
            .ToArray();
    }

    private static bool IsAllowedPolicyStatus(ToolExecutionStatus status)
    {
        return status is
            ToolExecutionStatus.Executed or
            ToolExecutionStatus.SkippedAlreadySatisfied or
            ToolExecutionStatus.SkippedDisabled or
            ToolExecutionStatus.Failed;
    }

    private static TResult? TryMapExecutionResult<TResult>(
        Func<TResult?> map,
        CancellationToken cancellationToken)
        where TResult : class
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var result = map();
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    internal static IReadOnlyList<ToolExecutionResult> ReconcileExecutionResults(
        IReadOnlyList<ApprovedToolCall> requestedCalls,
        IReadOnlyList<ToolExecutionResult>? returnedResults)
    {
        var matchedResults = new ToolExecutionResult?[requestedCalls.Count];
        var hasUnexpectedResult = false;

        foreach (var result in returnedResults ?? [])
        {
            if (result is null || result.ToolName is null)
            {
                hasUnexpectedResult = true;
                continue;
            }

            var matchedIndex = -1;
            for (var index = 0; index < requestedCalls.Count; index++)
            {
                if (matchedResults[index] is null &&
                    string.Equals(
                        requestedCalls[index].ToolName,
                        result.ToolName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    matchedIndex = index;
                    break;
                }
            }

            if (matchedIndex < 0)
            {
                hasUnexpectedResult = true;
                continue;
            }

            matchedResults[matchedIndex] = result;
        }

        if (hasUnexpectedResult)
        {
            return requestedCalls
                .Select(call => CreateFailedExecutionResult(
                    call,
                    UnexpectedExecutionResultsReason))
                .ToArray();
        }

        return requestedCalls
            .Select((call, index) =>
                matchedResults[index] ?? CreateMissingExecutionResult(call))
            .ToArray();
    }

    internal static (
        IReadOnlyList<ToolExecutionPolicyDecision> Data,
        IReadOnlyList<ToolExecutionPolicyDecision> Legal,
        IReadOnlyList<ToolExecutionPolicyDecision> Unsupported)
        PartitionPolicyDecisions(
            IReadOnlyList<ToolExecutionPolicyDecision> policyDecisions)
    {
        var data = new List<ToolExecutionPolicyDecision>();
        var legal = new List<ToolExecutionPolicyDecision>();
        var unsupported = new List<ToolExecutionPolicyDecision>();

        foreach (var decision in policyDecisions)
        {
            switch (PlannerToolCatalog.Find(decision.Call.ToolName)?.SatisfactionKind)
            {
                case PlannerToolSatisfactionKind.DataAnalysis:
                    data.Add(decision);
                    break;
                case PlannerToolSatisfactionKind.LegalReview:
                    legal.Add(decision);
                    break;
                default:
                    unsupported.Add(decision);
                    break;
            }
        }

        if (data.Count + legal.Count + unsupported.Count != policyDecisions.Count)
        {
            throw new InvalidOperationException(
                "Planner policy decision partition was not exhaustive.");
        }

        return (data, legal, unsupported);
    }

    private static IReadOnlyList<ToolExecutionResult> CreateUnsupportedExecutionAudit(
        IReadOnlyList<ToolExecutionPolicyDecision> unsupportedDecisions)
    {
        return unsupportedDecisions
            .Select(decision => decision.Status == ToolExecutionStatus.Executed
                ? new ToolExecutionResult(
                    ToolName: decision.Call.ToolName,
                    Status: ToolExecutionStatus.Failed,
                    Succeeded: false,
                    Summary: UnsupportedSatisfactionKindReason,
                    Engine: "Tool Execution Policy",
                    OutputJson: "{}",
                    Error: UnsupportedSatisfactionKindReason)
                : CreatePolicyAuditResult(decision))
            .ToArray();
    }

    private static string GetDataToolStatus(
        IReadOnlyList<ToolExecutionPolicyDecision> dataDecisions,
        IReadOnlyList<ToolExecutionResult> dataAudit)
    {
        if (dataDecisions.Count == 0)
        {
            return LegalDataToolStatuses.Absent;
        }

        return dataAudit.Any(result => result.Succeeded)
            ? LegalDataToolStatuses.Executed
            : LegalDataToolStatuses.Failed;
    }

    private static ToolExecutionResult CreateMissingExecutionResult(
        ApprovedToolCall call)
    {
        return CreateFailedExecutionResult(call, MissingExecutionResultReason);
    }

    private static ToolExecutionResult CreateFailedExecutionResult(
        ApprovedToolCall call,
        string reason)
    {
        return new ToolExecutionResult(
            ToolName: call.ToolName,
            Status: ToolExecutionStatus.Failed,
            Succeeded: false,
            Summary: reason,
            Engine: "Controlled Tool Executor",
            OutputJson: "{}",
            Error: reason
        );
    }

    private async Task PublishToolPlanAuditEventsAsync(
        Guid sessionId,
        ToolPlanAuditResult toolPlan,
        CancellationToken cancellationToken)
    {
        await PublishToolPlanValidationEventsAsync(
            sessionId,
            toolPlan,
            cancellationToken);
        await PublishToolExecutionAuditEventsAsync(
            sessionId,
            toolPlan.ExecutedCalls,
            cancellationToken);
    }

    private async Task PublishToolPlanValidationEventsAsync(
        Guid sessionId,
        ToolPlanAuditResult toolPlan,
        CancellationToken cancellationToken)
    {
        if (toolPlan.ProposedCalls.Count > 0)
        {
            await PublishAsync(
                sessionId,
                "tool_plan_proposed",
                "PlannerAgent",
                $"PlannerAgent propuso {toolPlan.ProposedCalls.Count} llamadas a herramientas.",
                cancellationToken
            );
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (toolPlan.ApprovedCalls.Count > 0 || toolPlan.RejectedCalls.Count > 0)
        {
            await PublishAsync(
                sessionId,
                "tool_plan_validated",
                "PlannerAgent",
                $"Plan de herramientas validado: {toolPlan.ApprovedCalls.Count} aprobadas, {toolPlan.RejectedCalls.Count} rechazadas.",
                cancellationToken
            );
            cancellationToken.ThrowIfCancellationRequested();
        }

        foreach (var rejectedCall in toolPlan.RejectedCalls)
        {
            await PublishAsync(
                sessionId,
                "tool_call_rejected",
                "PlannerAgent",
                $"Llamada a herramienta '{rejectedCall.ToolName}' rechazada: {rejectedCall.Reason}",
                cancellationToken
            );
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private async Task PublishToolExecutionAuditEventsAsync(
        Guid sessionId,
        IReadOnlyList<ToolExecutionResult> executionAudits,
        CancellationToken cancellationToken)
    {
        foreach (var executionAudit in executionAudits)
        {
            var auditActor = PlannerToolCatalog.Find(executionAudit.ToolName)?.AuditActor
                ?? "PlannerAgent";

            if (executionAudit.Status is ToolExecutionStatus.SkippedAlreadySatisfied or ToolExecutionStatus.SkippedDisabled)
            {
                await PublishAsync(
                    sessionId,
                    "tool_call_skipped",
                    auditActor,
                    $"Llamada a herramienta aprobada '{executionAudit.ToolName}' omitida: {executionAudit.Summary}",
                    cancellationToken
                );
            }

            if (executionAudit.Status == ToolExecutionStatus.Executed)
            {
                await PublishAsync(
                    sessionId,
                    "tool_call_executed",
                    auditActor,
                    $"Llamada a herramienta aprobada '{executionAudit.ToolName}' ejecutada usando {executionAudit.Engine}.",
                    cancellationToken
                );
            }

            if (executionAudit.Status == ToolExecutionStatus.Failed)
            {
                await PublishAsync(
                    sessionId,
                    "tool_call_failed",
                    auditActor,
                    $"Llamada a herramienta aprobada '{executionAudit.ToolName}' falló: {executionAudit.Error ?? executionAudit.Summary}",
                    cancellationToken
                );
            }

            cancellationToken.ThrowIfCancellationRequested();
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
            return "El razonamiento del LLM falló; se utilizó la alternativa determinista de contingencia.";
        }

        if (reasoningResult.UsedLlm)
        {
            return $"Razonamiento del Planificador completado usando {reasoningResult.Engine}.";
        }

        return "Razonamiento del Planificador completado usando la alternativa determinista.";
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
            SubmittedAt: report.SubmittedAt,
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
            SubmittedAt: report.SubmittedAt,
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
            SubmittedAt: report.SubmittedAt,
            PlannerSummary: "Recopilar evidencia de anomalías financieras y recuperación regulatoria antes del razonamiento del planificador.",
            RiskFactors: [],
            Limitations:
            [
                "El LLM puede proponer herramientas únicamente; el control del flujo de trabajo sigue siendo determinista.",
                "La aprobación humana sigue siendo obligatoria cuando existe riesgo."
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
