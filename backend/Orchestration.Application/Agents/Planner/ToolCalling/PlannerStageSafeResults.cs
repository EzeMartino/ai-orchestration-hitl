using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;

namespace Orchestration.Application.Agents.Planner.ToolCalling;

public static class PlannerStageSafeResults
{
    private const string Engine = "Controlled Tool Executor";

    public static DataAgentResult DataUnavailable()
    {
        return new DataAgentResult(
            HasAnomaly: false,
            Severity: "Unknown",
            Summary: "El análisis de datos no produjo un resultado utilizable.",
            Engine: Engine,
            Evidence: [],
            FinancialAnalysis: null,
            RequiresHumanReview: true
        );
    }

    public static LegalAgentResult LegalUnavailable()
    {
        return new LegalAgentResult(
            HasComplianceRisk: false,
            RiskLevel: "Unknown",
            Summary: "La revisión legal no produjo un resultado utilizable.",
            Engine: Engine,
            Evidence: [],
            Warnings:
            [
                "No hubo evidencia legal automatizada utilizable; se requiere revisión legal humana."
            ],
            RequiresHumanReview: true
        );
    }
}
