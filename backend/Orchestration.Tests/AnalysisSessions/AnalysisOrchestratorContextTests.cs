using System.Text.Json;
using FluentAssertions;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Planner;
using Orchestration.Application.Agents.Planner.Reasoning;
using Orchestration.Application.AnalysisSessions;

namespace Orchestration.Tests.AnalysisSessions;

public class AnalysisOrchestratorContextTests
{
    [Fact]
    public void BuildAnalysisContext_Should_include_planner_reasoning()
    {
        var plannerResult = new PlannerAgentResult(
            RequiresHumanApproval: true,
            Summary: "Human approval required.",
            DataResult: new DataAgentResult(
                HasAnomaly: true,
                Severity: "High",
                Summary: "Anomaly detected.",
                Engine: "TestDataEngine",
                Evidence: []
            ),
            LegalResult: new LegalAgentResult(
                HasComplianceRisk: true,
                RiskLevel: "Medium",
                Summary: "Compliance review required.",
                Engine: "TestLegalEngine",
                Evidence: [],
                Warnings: ["Human legal review required."]
            ),
            ReasoningResult: new PlannerReasoningResult(
                Engine: "Deterministic Planner Reasoning",
                Summary: "Planner reviewed collected evidence.",
                RecommendedActions: ["Review evidence."],
                RiskFactors: ["High data severity."],
                Limitations: ["No LLM reasoning was used."]
            )
        );

        var contextJson = AnalysisOrchestratorService.BuildAnalysisContext(plannerResult);

        using var document = JsonDocument.Parse(contextJson);
        var planner = document.RootElement.GetProperty("planner");

        planner.GetProperty("engine").GetString().Should().Be("Deterministic Planner Reasoning");
        planner.GetProperty("summary").GetString().Should().Be("Planner reviewed collected evidence.");
        planner.GetProperty("recommendedActions")[0].GetString().Should().Be("Review evidence.");
        planner.GetProperty("riskFactors")[0].GetString().Should().Be("High data severity.");
        planner.GetProperty("limitations")[0].GetString().Should().Be("No LLM reasoning was used.");
    }
}
