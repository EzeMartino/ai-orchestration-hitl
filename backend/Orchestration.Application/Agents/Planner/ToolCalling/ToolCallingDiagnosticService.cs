namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed class ToolCallingDiagnosticService : IToolCallingDiagnosticService
{
    private readonly IToolPlanNormalizer _normalizer;
    private readonly IToolPlanValidator _validator;
    private readonly IToolExecutionPolicy _policy;
    private readonly IControlledToolExecutor _executor;

    public ToolCallingDiagnosticService(
        IToolPlanNormalizer normalizer,
        IToolPlanValidator validator,
        IToolExecutionPolicy policy,
        IControlledToolExecutor executor)
    {
        _normalizer = normalizer;
        _validator = validator;
        _policy = policy;
        _executor = executor;
    }

    public async Task<ToolPlanAuditResult> ExecuteAsync(
        ToolCallingDiagnosticRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var proposedPlan = new ToolPlan(request.ProposedCalls ?? []);
        var normalizedPlan = _normalizer.Normalize(proposedPlan);
        var validationResult = _validator.Validate(normalizedPlan);
        var policyDecisions = _policy.Decide(
            validationResult.ApprovedCalls,
            new ToolExecutionPolicyContext(
                DataAnalysisAlreadyCompleted: false,
                LegalReviewAlreadyCompleted: false,
                DynamicExecutionEnabled: request.DynamicExecutionEnabled
            )
        );

        var executedCalls = await BuildExecutionAuditAsync(
            policyDecisions,
            cancellationToken
        );

        return new ToolPlanAuditResult(
            ProposedCalls: normalizedPlan.ProposedCalls,
            ApprovedCalls: validationResult.ApprovedCalls,
            RejectedCalls: validationResult.RejectedCalls,
            ExecutedCalls: executedCalls
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
        IReadOnlyList<ToolExecutionResult> executionResults = callsToExecute.Count == 0
            ? []
            : await _executor.ExecuteAsync(callsToExecute, cancellationToken);
        var executionIndex = 0;
        var executedCalls = new List<ToolExecutionResult>();

        foreach (var decision in policyDecisions)
        {
            if (decision.Status == ToolExecutionStatus.Executed)
            {
                executedCalls.Add(executionResults[executionIndex]);
                executionIndex++;
                continue;
            }

            executedCalls.Add(CreatePolicyAuditResult(decision));
        }

        return executedCalls;
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
}
