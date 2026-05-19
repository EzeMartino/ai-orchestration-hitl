namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed class ToolPlanValidator : IToolPlanValidator
{
    private const string NotAllowlistedReason = "Tool is not allowlisted.";
    private const string WorkflowReason = "Workflow transition tools are not allowed.";
    private const string ApprovalReason = "Human approval tools cannot be called by LLM.";
    private const string OperationalReason = "Operational financial tools are not allowed.";
    private const string LegalConclusionReason = "Legal conclusion tools are not allowed.";
    private const string MaxToolCallsReason = "Maximum tool call count exceeded.";
    private const string FinancialToolsDisabledReason = "Financial analysis tools are disabled.";
    private const string MissingRequestJsonReason = "Missing required argument: requestJson.";
    private const string EmptyRequestJsonReason = "Empty required argument: requestJson.";

    private readonly HashSet<string> _allowedTools;
    private readonly int _maxToolCalls;
    private readonly bool _financialAnalysisToolsEnabled;

    private static readonly HashSet<string> FinancialAnalysisTools = new(
        [
            "data.compute_financial_ratios",
            "data.compare_periods",
            "data.detect_financial_risk_signals",
            "data.summarize_quantitative_evidence"
        ],
        StringComparer.OrdinalIgnoreCase
    );

    public ToolPlanValidator(
        ToolCallingOptions? options = null)
    {
        var resolvedOptions = options ?? new ToolCallingOptions();

        _allowedTools = new HashSet<string>(
            resolvedOptions.AllowedTools
                .Where(tool => !string.IsNullOrWhiteSpace(tool)),
            StringComparer.OrdinalIgnoreCase
        );

        _maxToolCalls = Math.Max(0, resolvedOptions.MaxToolCalls);
        _financialAnalysisToolsEnabled = resolvedOptions.FinancialAnalysisToolsEnabled;
    }

    public ToolValidationResult Validate(
        ToolPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var approvedCalls = new List<ApprovedToolCall>();
        var rejectedCalls = new List<RejectedToolCall>();

        for (var index = 0; index < plan.ProposedCalls.Count; index++)
        {
            var proposedCall = plan.ProposedCalls[index];
            var toolName = proposedCall.ToolName;

            if (index >= _maxToolCalls)
            {
                rejectedCalls.Add(new RejectedToolCall(toolName, MaxToolCallsReason));
                continue;
            }

            var rejectionReason = GetRejectionReason(proposedCall);

            if (rejectionReason is not null)
            {
                rejectedCalls.Add(new RejectedToolCall(toolName, rejectionReason));
                continue;
            }

            approvedCalls.Add(new ApprovedToolCall(
                toolName,
                proposedCall.Arguments,
                proposedCall.Reason
            ));
        }

        return new ToolValidationResult(
            IsValid: rejectedCalls.Count == 0,
            ApprovedCalls: approvedCalls,
            RejectedCalls: rejectedCalls
        );
    }

    private string? GetRejectionReason(
        ProposedToolCall proposedCall)
    {
        var toolName = proposedCall.ToolName;

        if (string.IsNullOrWhiteSpace(toolName))
        {
            return NotAllowlistedReason;
        }

        if (toolName.StartsWith("workflow.", StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowReason;
        }

        if (toolName.StartsWith("approval.", StringComparison.OrdinalIgnoreCase))
        {
            return ApprovalReason;
        }

        if (toolName.StartsWith("money.", StringComparison.OrdinalIgnoreCase) ||
            toolName.StartsWith("account.", StringComparison.OrdinalIgnoreCase) ||
            toolName.StartsWith("transaction.", StringComparison.OrdinalIgnoreCase))
        {
            return OperationalReason;
        }

        if (string.Equals(toolName, "legal.determine_violation", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(toolName, "legal.issue_advice", StringComparison.OrdinalIgnoreCase))
        {
            return LegalConclusionReason;
        }

        if (FinancialAnalysisTools.Contains(toolName))
        {
            if (!_financialAnalysisToolsEnabled)
            {
                return FinancialToolsDisabledReason;
            }

            if (!TryFindArgument(proposedCall.Arguments, "requestJson", out var requestJson))
            {
                return MissingRequestJsonReason;
            }

            return string.IsNullOrWhiteSpace(requestJson)
                ? EmptyRequestJsonReason
                : null;
        }

        return _allowedTools.Contains(toolName)
            ? null
            : NotAllowlistedReason;
    }

    private static bool TryFindArgument(
        IReadOnlyDictionary<string, string> arguments,
        string key,
        out string value)
    {
        foreach (var argument in arguments)
        {
            if (string.Equals(argument.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = argument.Value;

                return true;
            }
        }

        value = "";

        return false;
    }
}
