namespace Orchestration.Application.Agents.Planner.Reasoning;

public sealed class DeterministicPlannerReasoningService : IPlannerReasoningService
{
    public Task<PlannerReasoningResult> GenerateReasoningAsync(
        PlannerReasoningInput input,
        CancellationToken cancellationToken)
    {
        var result = new PlannerReasoningResult(
            Engine: "Deterministic Planner Reasoning",
            Summary: "Se recopiló evidencia de riesgo de DataAgent y LegalAgent. Se requiere revisión humana antes de completar el workflow.",
            RecommendedActions:
            [
                "Revisar la evidencia cuantitativa de anomalías.",
                "Revisar la evidencia regulatoria citada de la CNV.",
                "Aprobar o rechazar la sesión según el criterio humano."
            ],
            RiskFactors:
            [
                $"Severidad de datos: {input.DataSeverity}",
                $"Nivel de riesgo legal: {input.LegalRiskLevel}"
            ],
            Limitations:
            [
                "No se usó razonamiento LLM.",
                "Esto no es asesoramiento legal, financiero ni de inversión.",
                "Se requiere aprobación humana antes de completar workflows riesgosos."
            ],
            UsedLlm: false,
            UsedFallback: true,
            Provider: null,
            Model: null,
            FailureReason: null
        );

        return Task.FromResult(result);
    }
}
