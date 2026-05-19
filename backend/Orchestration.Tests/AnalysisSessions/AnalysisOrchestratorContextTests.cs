using System.Text.Json;
using FluentAssertions;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
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

    [Fact]
    public void BuildAnalysisContext_Should_include_financial_analysis_when_available()
    {
        var financialContext = new FinancialAnalysisContext(
            Engine: "Semantic Kernel + CSnakes + Python/Pandas",
            DocumentId: "vista-energy-report-sample",
            Company: "Vista Energy",
            Ratios:
            [
                new FinancialRatio(
                    Name: "current_ratio",
                    Period: "2025E",
                    Value: 0.67m,
                    Unit: "x",
                    Formula: "current_assets / current_liabilities",
                    Inputs: ["current_assets", "current_liabilities"],
                    Interpretation: "Current ratio computed."
                )
            ],
            Comparisons:
            [
                new FinancialPeriodComparison(
                    MetricName: "revenue",
                    FromPeriod: "2024A",
                    ToPeriod: "2025E",
                    FromValue: 100m,
                    ToValue: 80m,
                    AbsoluteChange: -20m,
                    PercentageChange: -0.2m,
                    Unit: "USD millions",
                    Interpretation: "Revenue declined."
                )
            ],
            RiskSignals:
            [
                new FinancialRiskSignal(
                    Name: "LOW_CURRENT_RATIO",
                    Severity: "High",
                    Period: "2025E",
                    Summary: "Current ratio below threshold. Human review recommended.",
                    Evidence:
                    [
                        new RiskEvidenceItem(
                            MetricName: "current_ratio",
                            Period: "2025E",
                            Value: 0.67m,
                            Threshold: 1.0m,
                            Unit: "x",
                            Interpretation: "Current ratio below 1.0 may indicate liquidity pressure."
                        )
                    ]
                )
            ],
            RiskEvidence:
            [
                new RiskEvidenceItem(
                    MetricName: "current_ratio",
                    Period: "2025E",
                    Value: 0.67m,
                    Threshold: 1.0m,
                    Unit: "x",
                    Interpretation: "Current ratio below 1.0 may indicate liquidity pressure."
                )
            ],
            Warnings: ["Structured metrics only."],
            Limitations: ["No PDF parsing or OCR was performed."]
        );
        var plannerResult = CreatePlannerResult(
            ToolPlanAuditResult.Empty,
            financialContext
        );

        var contextJson = AnalysisOrchestratorService.BuildAnalysisContext(plannerResult);

        using var document = JsonDocument.Parse(contextJson);
        var root = document.RootElement;
        var financialAnalysis = root.GetProperty("financialAnalysis");

        financialAnalysis.GetProperty("engine").GetString().Should().Be("Semantic Kernel + CSnakes + Python/Pandas");
        financialAnalysis.GetProperty("documentId").GetString().Should().Be("vista-energy-report-sample");
        financialAnalysis.GetProperty("company").GetString().Should().Be("Vista Energy");
        financialAnalysis.GetProperty("ratios")[0].GetProperty("name").GetString().Should().Be("current_ratio");
        financialAnalysis.GetProperty("ratios")[0].GetProperty("source").GetString().Should().Be("computed");
        financialAnalysis.GetProperty("ratios")[0].GetProperty("inputMetrics")[0].GetString().Should().Be("current_assets");
        financialAnalysis.GetProperty("comparisons")[0].GetProperty("metricName").GetString().Should().Be("revenue");
        financialAnalysis.GetProperty("riskSignals")[0].GetProperty("code").GetString().Should().Be("LOW_CURRENT_RATIO");
        financialAnalysis.GetProperty("riskSignals")[0].GetProperty("severity").GetString().Should().Be("High");
        financialAnalysis.GetProperty("riskEvidence")[0].GetProperty("severity").GetString().Should().Be("High");
        financialAnalysis.GetProperty("riskEvidence")[0].GetProperty("engine").GetString().Should().Be("Semantic Kernel + CSnakes + Python/Pandas");
        financialAnalysis.GetProperty("warnings")[0].GetString().Should().Be("Structured metrics only.");
        financialAnalysis.GetProperty("limitations")[0].GetString().Should().Be("No PDF parsing or OCR was performed.");

        root.GetProperty("anomaly").GetProperty("summary").GetString().Should().Be("Anomaly detected.");
    }

    [Fact]
    public void BuildAnalysisContext_Should_remain_compatible_when_financial_analysis_is_null()
    {
        var contextJson = AnalysisOrchestratorService.BuildAnalysisContext(
            CreatePlannerResult(ToolPlanAuditResult.Empty)
        );

        using var document = JsonDocument.Parse(contextJson);

        document.RootElement.GetProperty("financialAnalysis").ValueKind
            .Should()
            .Be(JsonValueKind.Null);
        document.RootElement.GetProperty("anomaly").GetProperty("summary").GetString()
            .Should()
            .Be("Anomaly detected.");
    }

    private static PlannerAgentResult CreatePlannerResult(
        ToolPlanAuditResult toolPlan,
        FinancialAnalysisContext? financialAnalysisContext = null)
    {
        return new PlannerAgentResult(
            RequiresHumanApproval: true,
            Summary: "Human approval required.",
            DataResult: new DataAgentResult(
                HasAnomaly: true,
                Severity: "High",
                Summary: "Anomaly detected.",
                Engine: "TestDataEngine",
                Evidence: [],
                FinancialAnalysis: financialAnalysisContext
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
