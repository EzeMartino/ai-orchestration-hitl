using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Planner;
using Orchestration.Application.Agents.Planner.Reasoning;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.Agents.Shared;
using Orchestration.Application.AnalysisSessions;
using Orchestration.Application.FinancialAnalysis.Thresholds;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Tests.Agents;
using Orchestration.Tests.Agents.Data.FinancialAnalysis;

namespace Orchestration.Tests.AnalysisSessions;

public class AnalysisOrchestratorContextTests
{
    [Fact]
    public async Task StartAnalysisAsync_InvalidReportContext_ShouldFailWithoutInvokingPlanner()
    {
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var session = AnalysisSession.Create(Guid.NewGuid());
        session.SetContext("""{"financialReport":{"reportName":"","totalAmount":-1}}""");
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var publisher = new FakeActivityEventPublisher();
        var planner = new CapturingPlannerAgent();
        var orchestrator = new AnalysisOrchestratorService(
            dbContext,
            new AnalysisSessionWorkflowService(new AnalysisSessionStateMachine()),
            publisher,
            planner,
            new FinancialReportContextResolver());

        var result = await orchestrator.StartAnalysisAsync(
            session.Id,
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Status.Should().Be(nameof(AnalysisSessionStatus.Failed));
        planner.RunCalls.Should().Be(0);
        session.Status.Should().Be(AnalysisSessionStatus.Failed);
        session.CurrentAgent.Should().BeNull();
        session.FailureReason.Should().Be("Financial report summary is invalid.");
        publisher.PublishedEvents.Should().ContainSingle(evt =>
            evt.Type == "financial_report_context_invalid" &&
            evt.Agent == "Orchestrator");
        var persisted = await dbContext.AnalysisSessions
            .AsNoTracking()
            .SingleAsync(item => item.Id == session.Id);
        persisted.Status.Should().Be(AnalysisSessionStatus.Failed);
    }

    [Theory]
    [InlineData(AnalysisSessionStatus.Completed)]
    [InlineData(AnalysisSessionStatus.Failed)]
    public async Task StartAnalysisAsync_TerminalSessionWithInvalidReport_ShouldPreserveState(
        AnalysisSessionStatus terminalStatus)
    {
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var session = AnalysisSession.Create(Guid.NewGuid());
        const string originalContext = """{"financialReport":{"reportName":""}}""";
        session.SetContext(originalContext);
        session.SetCurrentAgent("ExistingAgent");

        if (terminalStatus == AnalysisSessionStatus.Failed)
        {
            session.MarkFailed("Existing failure");
        }
        else
        {
            session.SetStatus(terminalStatus);
        }

        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var originalFailureReason = session.FailureReason;
        var originalUpdatedAt = session.UpdatedAt;
        var originalCompletedAt = session.CompletedAt;
        var publisher = new FakeActivityEventPublisher();
        var planner = new CapturingPlannerAgent();
        var orchestrator = new AnalysisOrchestratorService(
            dbContext,
            new AnalysisSessionWorkflowService(new AnalysisSessionStateMachine()),
            publisher,
            planner,
            new FinancialReportContextResolver());

        Func<Task> act = async () => await orchestrator.StartAnalysisAsync(
            session.Id,
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Invalid transition: {terminalStatus} -> Start");
        planner.RunCalls.Should().Be(0);
        publisher.PublishedEvents.Should().BeEmpty();
        session.Status.Should().Be(terminalStatus);
        session.ContextJson.Should().Be(originalContext);
        session.FailureReason.Should().Be(originalFailureReason);
        session.CurrentAgent.Should().Be("ExistingAgent");
        session.UpdatedAt.Should().Be(originalUpdatedAt);
        session.CompletedAt.Should().Be(originalCompletedAt);

        var persisted = await dbContext.AnalysisSessions
            .AsNoTracking()
            .SingleAsync(item => item.Id == session.Id);
        persisted.Status.Should().Be(terminalStatus);
        persisted.ContextJson.Should().Be(originalContext);
        persisted.FailureReason.Should().Be(originalFailureReason);
        persisted.CurrentAgent.Should().Be("ExistingAgent");
    }

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
                Limitations: ["No se usó razonamiento LLM."],
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
        planner.GetProperty("limitations")[0].GetString().Should().Be("No se usó razonamiento LLM.");
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
                    "No se permiten herramientas de transición de workflow."
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
        rejectedCall.GetProperty("reason").GetString().Should().Be("No se permiten herramientas de transición de workflow.");

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
                    ],
                    Metric: "current_ratio",
                    Value: 0.67m,
                    ThresholdCode: "LOW_CURRENT_RATIO",
                    ThresholdOperator: "<",
                    ThresholdValue: 1.0m,
                    Reason: "current_ratio 0.67 crossed the configured threshold < 1.0."
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
            Limitations: ["No PDF parsing or OCR was performed."],
            MetricsInputSource: FinancialMetricsInputSources.SessionContext,
            MetricsProvenance: new StructuredFinancialMetricsProvenance(
                IngestionMethod: "json_file",
                OriginalFileName: "metrics.json",
                FileSizeBytes: 1024,
                ContentHash: "abc123",
                MetricCount: 10,
                WarningCount: 1
            ),
            AiReview: new FinancialAnalysisAiReviewResult(
                Summary: "Advisory AI review summarized deterministic evidence.",
                KeyFindings:
                [
                    new FinancialAnalysisAiKeyFinding(
                        Title: "Liquidity pressure",
                        Description: "Current ratio is below threshold.",
                        Severity: "High",
                        RelatedMetrics: ["current_ratio"]
                    )
                ],
                RiskInterpretation: "Human review should focus on liquidity evidence.",
                DataQualityNotes:
                [
                    new FinancialAnalysisAiDataQualityNote(
                        Message: "Structured metrics only.",
                        Severity: "Info",
                        RelatedFields: ["warnings"]
                    )
                ],
                Limitations: ["AI review is advisory."],
                UsedLlm: false,
                UsedFallback: true,
                Provider: null,
                Model: null,
                FailureReason: null
            ),
            ThresholdProfile: "strict",
            ThresholdsUsed:
            [
                new FinancialRiskThreshold("LOW_CURRENT_RATIO", "current_ratio", "<", 1.5m, "Medium", "Current ratio is below 1.5. Human review recommended.")
            ]
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
        financialAnalysis.GetProperty("riskSignals")[0].GetProperty("metric").GetString().Should().Be("current_ratio");
        financialAnalysis.GetProperty("riskSignals")[0].GetProperty("value").GetDecimal().Should().Be(0.67m);
        financialAnalysis.GetProperty("riskSignals")[0].GetProperty("thresholdCode").GetString().Should().Be("LOW_CURRENT_RATIO");
        financialAnalysis.GetProperty("riskSignals")[0].GetProperty("thresholdOperator").GetString().Should().Be("<");
        financialAnalysis.GetProperty("riskSignals")[0].GetProperty("thresholdValue").GetDecimal().Should().Be(1.0m);
        financialAnalysis.GetProperty("riskSignals")[0].GetProperty("reason").GetString().Should().Be("current_ratio 0.67 crossed the configured threshold < 1.0.");
        financialAnalysis.GetProperty("riskEvidence")[0].GetProperty("severity").GetString().Should().Be("High");
        financialAnalysis.GetProperty("riskEvidence")[0].GetProperty("engine").GetString().Should().Be("Semantic Kernel + CSnakes + Python/Pandas");
        financialAnalysis.GetProperty("warnings")[0].GetString().Should().Be("Structured metrics only.");
        financialAnalysis.GetProperty("limitations")[0].GetString().Should().Be("No PDF parsing or OCR was performed.");
        financialAnalysis.GetProperty("metricsInputSource").GetString().Should().Be("session_context");
        financialAnalysis.GetProperty("metricsProvenance").GetProperty("ingestionMethod").GetString().Should().Be("json_file");
        financialAnalysis.GetProperty("metricsProvenance").GetProperty("originalFileName").GetString().Should().Be("metrics.json");
        var aiReview = financialAnalysis.GetProperty("aiReview");
        aiReview.GetProperty("summary").GetString().Should().Be("Advisory AI review summarized deterministic evidence.");
        aiReview.GetProperty("keyFindings")[0].GetProperty("title").GetString().Should().Be("Liquidity pressure");
        aiReview.GetProperty("keyFindings")[0].GetProperty("relatedMetrics")[0].GetString().Should().Be("current_ratio");
        aiReview.GetProperty("riskInterpretation").GetString().Should().Be("Human review should focus on liquidity evidence.");
        aiReview.GetProperty("dataQualityNotes")[0].GetProperty("message").GetString().Should().Be("Structured metrics only.");
        aiReview.GetProperty("limitations")[0].GetString().Should().Be("AI review is advisory.");
        aiReview.GetProperty("usedLlm").GetBoolean().Should().BeFalse();
        aiReview.GetProperty("usedFallback").GetBoolean().Should().BeTrue();
        aiReview.GetProperty("failureReason").ValueKind.Should().Be(JsonValueKind.Null);

        financialAnalysis.GetProperty("thresholdProfile").GetString().Should().Be("strict");
        var thresholdsUsed = financialAnalysis.GetProperty("thresholdsUsed");
        thresholdsUsed.GetArrayLength().Should().Be(1);
        thresholdsUsed[0].GetProperty("code").GetString().Should().Be("LOW_CURRENT_RATIO");
        thresholdsUsed[0].GetProperty("metric").GetString().Should().Be("current_ratio");
        thresholdsUsed[0].GetProperty("operator").GetString().Should().Be("<");
        thresholdsUsed[0].GetProperty("value").GetDecimal().Should().Be(1.5m);
        thresholdsUsed[0].GetProperty("severity").GetString().Should().Be("Medium");
        thresholdsUsed[0].GetProperty("description").GetString().Should().Be("Current ratio is below 1.5. Human review recommended.");

        root.GetProperty("anomaly").GetProperty("summary").GetString().Should().Be("Anomaly detected.");
    }

    [Fact]
    public void FinancialAnalysisContext_Should_deserialize_old_json_without_ai_review()
    {
        const string json = """
        {
          "engine": "Semantic Kernel + CSnakes + Python/Pandas",
          "documentId": "old-session",
          "company": "Old Co",
          "ratios": [],
          "comparisons": [],
          "riskSignals": [],
          "riskEvidence": [],
          "warnings": [],
          "limitations": [],
          "metricsInputSource": "session_context",
          "metricsProvenance": null
        }
        """;

        var context = JsonSerializer.Deserialize<FinancialAnalysisContext>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );

        context.Should().NotBeNull();
        context!.AiReview.Should().BeNull();
        context.DocumentId.Should().Be("old-session");
    }

    [Fact]
    public void FinancialAnalysisContext_Should_deserialize_old_json_without_thresholds()
    {
        const string json = """
        {
          "engine": "Semantic Kernel + CSnakes + Python/Pandas",
          "documentId": "old-session",
          "company": "Old Co",
          "ratios": [],
          "comparisons": [],
          "riskSignals": [],
          "riskEvidence": [],
          "warnings": [],
          "limitations": [],
          "metricsInputSource": "session_context",
          "metricsProvenance": null
        }
        """;

        var context = JsonSerializer.Deserialize<FinancialAnalysisContext>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );

        context.Should().NotBeNull();
        context!.ThresholdProfile.Should().BeNull();
        context.ThresholdsUsed.Should().NotBeNull().And.BeEmpty();
        context.Execution.OverallStatus.Should().Be(FinancialAnalysisExecutionStatus.LegacyUnknown);
        context.Execution.Stages.Should().BeEmpty();
        context.DocumentId.Should().Be("old-session");
    }

    [Fact]
    public void BuildAnalysisContext_Should_persist_inconclusive_degraded_execution_evidence()
    {
        var execution = FinancialAnalysisExecution.FromStages(
        [
            new FinancialAnalysisStageExecution(
                FinancialAnalysisOperations.Ratios,
                FinancialAnalysisExecutionStatus.Failed,
                17,
                FinancialAnalysisFailureCodes.PythonInvocationFailed),
            new FinancialAnalysisStageExecution(
                FinancialAnalysisOperations.Comparisons,
                FinancialAnalysisExecutionStatus.Succeeded,
                8),
            new FinancialAnalysisStageExecution(
                FinancialAnalysisOperations.Signals,
                FinancialAnalysisExecutionStatus.Succeeded,
                5),
            new FinancialAnalysisStageExecution(
                FinancialAnalysisOperations.Summary,
                FinancialAnalysisExecutionStatus.Succeeded,
                3)
        ]);
        var financialContext = new FinancialAnalysisContext(
            Engine: "Test financial engine",
            DocumentId: "degraded-document",
            Company: "Degraded Co",
            Ratios: [],
            Comparisons: [],
            RiskSignals: [],
            RiskEvidence: [],
            Warnings: [],
            Limitations: [])
        {
            Execution = execution
        };
        var plannerResult = CreatePlannerResult(
            ToolPlanAuditResult.Empty,
            financialContext,
            hasAnomaly: false,
            requiresHumanReview: true);

        var contextJson = AnalysisOrchestratorService.BuildAnalysisContext(plannerResult);

        using var document = JsonDocument.Parse(contextJson);
        var anomaly = document.RootElement.GetProperty("anomaly");
        anomaly.GetProperty("detected").GetBoolean().Should().BeFalse();
        anomaly.GetProperty("assessmentStatus").GetString().Should().Be("inconclusive");
        anomaly.GetProperty("requiresHumanReview").GetBoolean().Should().BeTrue();

        var persistedExecution = document.RootElement
            .GetProperty("financialAnalysis")
            .GetProperty("execution");
        persistedExecution.GetProperty("overallStatus").GetString().Should().Be("degraded");
        var ratioStage = persistedExecution.GetProperty("stages")
            .EnumerateArray()
            .Single(stage => stage.GetProperty("operation").GetString() == FinancialAnalysisOperations.Ratios);
        ratioStage.GetProperty("status").GetString().Should().Be("failed");
        ratioStage.GetProperty("durationMilliseconds").GetInt64().Should().Be(17);
        ratioStage.GetProperty("failureCode").GetString()
            .Should().Be(FinancialAnalysisFailureCodes.PythonInvocationFailed);
        persistedExecution.GetRawText().ToLowerInvariant().Should().NotContain("exception");
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

    [Fact]
    public void BuildAnalysisContext_Should_preserve_structured_financial_metrics()
    {
        const string existingContextJson = """
        {
          "financialReport": {
            "reportName": "persisted-report.pdf",
            "totalAmount": 842350.75,
            "transactionCount": 187,
            "submittedAt": "2026-07-12T18:30:00Z"
          },
          "structuredFinancialMetrics": {
            "documentId": "uploaded-json-metrics-test",
            "company": "Uploaded JSON Test Co",
            "currency": "USD",
            "unit": "USD_thousand",
            "metrics": [
              {
                "name": "revenue",
                "period": "2024A",
                "value": 1647768,
                "unit": "USD_thousand",
                "currency": "USD",
                "source": "manual_upload",
                "sourcePage": 18,
                "confidence": 0.9
              }
            ],
            "validationWarnings": [],
            "uploadedAt": "2026-05-20T00:00:00Z"
          }
        }
        """;

        var contextJson = AnalysisOrchestratorService.BuildAnalysisContext(
            CreatePlannerResult(ToolPlanAuditResult.Empty),
            existingContextJson
        );

        using var document = JsonDocument.Parse(contextJson);
        var structuredMetrics = document.RootElement.GetProperty("structuredFinancialMetrics");
        var financialReport = document.RootElement.GetProperty("financialReport");

        financialReport.GetProperty("reportName").GetString()
            .Should()
            .Be("persisted-report.pdf");
        financialReport.GetProperty("totalAmount").GetDecimal()
            .Should()
            .Be(842350.75m);

        structuredMetrics.GetProperty("documentId").GetString()
            .Should()
            .Be("uploaded-json-metrics-test");
        structuredMetrics.GetProperty("company").GetString()
            .Should()
            .Be("Uploaded JSON Test Co");
        structuredMetrics.GetProperty("metrics")[0].GetProperty("name").GetString()
            .Should()
            .Be("revenue");

        document.RootElement.GetProperty("planner").GetProperty("summary").GetString()
            .Should()
            .Be("Planner reviewed collected evidence.");
    }

    private static PlannerAgentResult CreatePlannerResult(
        ToolPlanAuditResult toolPlan,
        FinancialAnalysisContext? financialAnalysisContext = null,
        bool hasAnomaly = true,
        bool requiresHumanReview = false)
    {
        return new PlannerAgentResult(
            RequiresHumanApproval: true,
            Summary: "Human approval required.",
            DataResult: new DataAgentResult(
                HasAnomaly: hasAnomaly,
                Severity: hasAnomaly ? "High" : "Unknown",
                Summary: "Anomaly detected.",
                Engine: "TestDataEngine",
                Evidence: [],
                FinancialAnalysis: financialAnalysisContext,
                RequiresHumanReview: requiresHumanReview
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
                Limitations: ["No se usó razonamiento LLM."],
                UsedLlm: false,
                UsedFallback: true,
                Provider: null,
                Model: null,
                FailureReason: null
            ),
            ToolPlan: toolPlan
        );
    }

    private sealed class CapturingPlannerAgent : IPlannerAgent
    {
        public int RunCalls { get; private set; }

        public Task<PlannerAgentResult> RunAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            RunCalls++;
            throw new InvalidOperationException("Planner must not run for invalid context.");
        }
    }
}
