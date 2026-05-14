namespace Orchestration.Application.Agents.Planner.Reasoning;

public sealed class DeterministicPlannerReasoningService : IPlannerReasoningService
{
    public Task<PlannerReasoningResult> GenerateReasoningAsync(
        PlannerReasoningInput input,
        CancellationToken cancellationToken)
    {
        var result = new PlannerReasoningResult(
            Engine: "Deterministic Planner Reasoning",
            Summary: "Risk evidence was collected from DataAgent and LegalAgent. Human review is required before completing the workflow.",
            RecommendedActions:
            [
                "Review quantitative anomaly evidence.",
                "Review cited CNV regulatory evidence.",
                "Approve or reject the session based on human judgment."
            ],
            RiskFactors:
            [
                $"Data severity: {input.DataSeverity}",
                $"Legal risk level: {input.LegalRiskLevel}"
            ],
            Limitations:
            [
                "No LLM reasoning was used.",
                "This is not legal, financial, or investment advice."
            ]
        );

        return Task.FromResult(result);
    }
}
