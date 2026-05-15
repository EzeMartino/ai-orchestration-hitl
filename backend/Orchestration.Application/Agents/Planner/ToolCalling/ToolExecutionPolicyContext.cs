namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed record ToolExecutionPolicyContext(
    bool DataAnalysisAlreadyCompleted,
    bool LegalReviewAlreadyCompleted,
    bool DynamicExecutionEnabled
);
