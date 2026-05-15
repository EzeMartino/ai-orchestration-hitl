namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed class ToolExecutionPolicy : IToolExecutionPolicy
{
    private const string DataToolName = "data.analyze_transactions";
    private const string LegalToolName = "legal.search_cnv_regulation";

    public IReadOnlyList<ToolExecutionPolicyDecision> Decide(
        IReadOnlyList<ApprovedToolCall> approvedCalls,
        ToolExecutionPolicyContext context)
    {
        ArgumentNullException.ThrowIfNull(approvedCalls);
        ArgumentNullException.ThrowIfNull(context);

        return approvedCalls
            .Select(call => Decide(call, context))
            .ToList();
    }

    private static ToolExecutionPolicyDecision Decide(
        ApprovedToolCall call,
        ToolExecutionPolicyContext context)
    {
        if (string.Equals(call.ToolName, DataToolName, StringComparison.OrdinalIgnoreCase) &&
            context.DataAnalysisAlreadyCompleted)
        {
            return new ToolExecutionPolicyDecision(
                call,
                ToolExecutionStatus.SkippedAlreadySatisfied,
                "DataAgent already executed during the deterministic workflow."
            );
        }

        if (string.Equals(call.ToolName, LegalToolName, StringComparison.OrdinalIgnoreCase) &&
            context.LegalReviewAlreadyCompleted)
        {
            return new ToolExecutionPolicyDecision(
                call,
                ToolExecutionStatus.SkippedAlreadySatisfied,
                "LegalAgent already executed during the deterministic workflow."
            );
        }

        if (!context.DynamicExecutionEnabled)
        {
            return new ToolExecutionPolicyDecision(
                call,
                ToolExecutionStatus.SkippedDisabled,
                "Dynamic tool execution is disabled."
            );
        }

        return new ToolExecutionPolicyDecision(
            call,
            ToolExecutionStatus.Executed,
            "Tool is approved for controlled execution."
        );
    }
}
