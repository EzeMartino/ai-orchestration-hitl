using System.Text.Json;
using FluentAssertions;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Planner;
using Orchestration.Application.Agents.Planner.Reasoning;
using Orchestration.Application.Agents.Planner.ToolCalling;
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
                Limitations: ["No LLM reasoning was used."],
                UsedLlm: false,
                UsedFallback: true,
                Provider: "OpenAI",
                Model: "test-model",
                FailureReason: "LLM returned invalid JSON."
            ),
            ToolPlan: ToolPlanAuditResult.Empty
        );

        var contextJson = AnalysisOrchestratorService.BuildAnalysisContext(plannerResult);

        using var document = JsonDocument.Parse(contextJson);
        var planner = document.RootElement.GetProperty("planner");

        planner.GetProperty("engine").GetString().Should().Be("Deterministic Planner Reasoning");
        planner.GetProperty("summary").GetString().Should().Be("Planner reviewed collected evidence.");
        planner.GetProperty("recommendedActions")[0].GetString().Should().Be("Review evidence.");
        planner.GetProperty("riskFactors")[0].GetString().Should().Be("High data severity.");
        planner.GetProperty("limitations")[0].GetString().Should().Be("No LLM reasoning was used.");
        planner.GetProperty("usedLlm").GetBoolean().Should().BeFalse();
        planner.GetProperty("usedFallback").GetBoolean().Should().BeTrue();
        planner.GetProperty("provider").GetString().Should().Be("OpenAI");
        planner.GetProperty("model").GetString().Should().Be("test-model");
        planner.GetProperty("failureReason").GetString().Should().Be("LLM returned invalid JSON.");
    }

    [Fact]
    public void BuildAnalysisContext_Should_include_empty_tool_plan_block()
    {
        var plannerResult = CreatePlannerResult(ToolPlanAuditResult.Empty);

        var contextJson = AnalysisOrchestratorService.BuildAnalysisContext(plannerResult);

        using var document = JsonDocument.Parse(contextJson);
        var toolPlan = document.RootElement.GetProperty("toolPlan");

        toolPlan.GetProperty("proposedCalls").GetArrayLength().Should().Be(0);
        toolPlan.GetProperty("approvedCalls").GetArrayLength().Should().Be(0);
        toolPlan.GetProperty("rejectedCalls").GetArrayLength().Should().Be(0);
        toolPlan.GetProperty("executedCalls").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void BuildAnalysisContext_Should_map_tool_plan_calls_without_output_json()
    {
        var toolPlan = new ToolPlanAuditResult(
            ProposedCalls:
            [
                new ProposedToolCall(
                    "data.analyze_transactions",
                    new Dictionary<string, string>
                    {
                        ["sessionId"] = Guid.NewGuid().ToString()
                    },
                    "Collect anomaly evidence."
                )
            ],
            ApprovedCalls:
            [
                new ApprovedToolCall(
                    "legal.search_cnv_regulation",
                    new Dictionary<string, string>
                    {
                        ["query"] = "agentes"
                    },
                    "Retrieve cited CNV evidence."
                )
            ],
            RejectedCalls:
            [
                new RejectedToolCall(
                    "workflow.complete",
                    "Workflow transition tools are not allowed."
                )
            ],
            ExecutedCalls:
            [
                new ToolExecutionResult(
                    ToolName: "legal.search_cnv_regulation",
                    Status: ToolExecutionStatus.SkippedAlreadySatisfied,
                    Succeeded: true,
                    Summary: "LegalAgent already executed during the deterministic workflow.",
                    Engine: "Tool Execution Policy",
                    OutputJson: "{\"large\":\"payload\"}",
                    Error: null
                )
            ]
        );

        var contextJson = AnalysisOrchestratorService.BuildAnalysisContext(
            CreatePlannerResult(toolPlan)
        );

        using var document = JsonDocument.Parse(contextJson);
        var toolPlanElement = document.RootElement.GetProperty("toolPlan");

        var proposedCall = toolPlanElement.GetProperty("proposedCalls")[0];
        proposedCall.GetProperty("toolName").GetString().Should().Be("data.analyze_transactions");
        proposedCall.GetProperty("arguments").GetProperty("sessionId").GetString().Should().NotBeNullOrWhiteSpace();
        proposedCall.GetProperty("reason").GetString().Should().Be("Collect anomaly evidence.");

        var approvedCall = toolPlanElement.GetProperty("approvedCalls")[0];
        approvedCall.GetProperty("toolName").GetString().Should().Be("legal.search_cnv_regulation");
        approvedCall.GetProperty("arguments").GetProperty("query").GetString().Should().Be("agentes");
        approvedCall.GetProperty("reason").GetString().Should().Be("Retrieve cited CNV evidence.");

        var rejectedCall = toolPlanElement.GetProperty("rejectedCalls")[0];
        rejectedCall.GetProperty("toolName").GetString().Should().Be("workflow.complete");
        rejectedCall.GetProperty("reason").GetString().Should().Be("Workflow transition tools are not allowed.");

        var executedCall = toolPlanElement.GetProperty("executedCalls")[0];
        executedCall.GetProperty("toolName").GetString().Should().Be("legal.search_cnv_regulation");
        executedCall.GetProperty("status").GetString().Should().Be("SkippedAlreadySatisfied");
        executedCall.GetProperty("succeeded").GetBoolean().Should().BeTrue();
        executedCall.GetProperty("summary").GetString().Should().Be("LegalAgent already executed during the deterministic workflow.");
        executedCall.GetProperty("engine").GetString().Should().Be("Tool Execution Policy");
        executedCall.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
        executedCall.TryGetProperty("outputJson", out _).Should().BeFalse();
    }

    private static PlannerAgentResult CreatePlannerResult(
        ToolPlanAuditResult toolPlan)
    {
        return new PlannerAgentResult(
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
                Limitations: ["No LLM reasoning was used."],
                UsedLlm: false,
                UsedFallback: true,
                Provider: null,
                Model: null,
                FailureReason: null
            ),
            ToolPlan: toolPlan
        );
    }
}
