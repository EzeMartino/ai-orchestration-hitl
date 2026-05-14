namespace Orchestration.Application.Agents.Planner.Reasoning;

public sealed record PlannerReasoningResult(
    string Engine,
    string Summary,
    IReadOnlyList<string> RecommendedActions,
    IReadOnlyList<string> RiskFactors,
    IReadOnlyList<string> Limitations
);
