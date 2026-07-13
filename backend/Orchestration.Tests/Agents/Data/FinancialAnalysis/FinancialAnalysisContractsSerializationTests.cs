using System.Text.Json;
using FluentAssertions;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public class FinancialAnalysisContractsSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(FinancialAnalysisExecutionStatus.LegacyUnknown, "legacy_unknown")]
    [InlineData(FinancialAnalysisExecutionStatus.Succeeded, "succeeded")]
    [InlineData(FinancialAnalysisExecutionStatus.Degraded, "degraded")]
    [InlineData(FinancialAnalysisExecutionStatus.Failed, "failed")]
    public void Execution_status_Should_round_trip_as_lowercase_json(
        FinancialAnalysisExecutionStatus status,
        string expectedJsonValue)
    {
        var json = JsonSerializer.Serialize(status, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<FinancialAnalysisExecutionStatus>(
            json,
            JsonOptions
        );

        json.Should().Be($"\"{expectedJsonValue}\"");
        roundTripped.Should().Be(status);
    }

    [Theory]
    [InlineData("\"future_status\"")]
    [InlineData("42")]
    [InlineData("{\"unexpected\":true}")]
    public void Execution_status_Should_default_unknown_or_non_string_json_to_legacy_unknown(
        string json)
    {
        var status = JsonSerializer.Deserialize<FinancialAnalysisExecutionStatus>(
            json,
            JsonOptions
        );

        status.Should().Be(FinancialAnalysisExecutionStatus.LegacyUnknown);
    }

    [Fact]
    public void Old_response_json_without_execution_Should_default_each_operation_to_legacy_unknown()
    {
        const string resultJson = """
        {
          "hasRiskSignals": false,
          "riskLevel": "Low",
          "summary": "Legacy result.",
          "engine": "Legacy",
          "evidence": [],
          "warnings": []
        }
        """;
        var ratios = JsonSerializer.Deserialize<ComputeFinancialRatiosResponse>(
            """{"engine":"Legacy","ratios":[],"warnings":[]}""",
            JsonOptions
        );
        var comparisons = JsonSerializer.Deserialize<ComparePeriodsResponse>(
            """{"engine":"Legacy","comparisons":[],"warnings":[]}""",
            JsonOptions
        );
        var signals = JsonSerializer.Deserialize<DetectFinancialRiskSignalsResponse>(
            $$"""{"engine":"Legacy","signals":[],"result":{{resultJson}}}""",
            JsonOptions
        );
        var summary = JsonSerializer.Deserialize<SummarizeQuantitativeEvidenceResponse>(
            $$"""{"engine":"Legacy","narrative":"Legacy summary.","result":{{resultJson}}}""",
            JsonOptions
        );

        ratios!.Execution.Should().Be(
            FinancialAnalysisStageExecution.LegacyUnknown(FinancialAnalysisOperations.Ratios)
        );
        comparisons!.Execution.Should().Be(
            FinancialAnalysisStageExecution.LegacyUnknown(FinancialAnalysisOperations.Comparisons)
        );
        signals!.Execution.Should().Be(
            FinancialAnalysisStageExecution.LegacyUnknown(FinancialAnalysisOperations.Signals)
        );
        summary!.Execution.Should().Be(
            FinancialAnalysisStageExecution.LegacyUnknown(FinancialAnalysisOperations.Summary)
        );
    }

    [Fact]
    public void Old_financial_analysis_context_json_without_execution_Should_default_to_legacy_unknown()
    {
        const string json = """
        {
          "engine": "Legacy",
          "documentId": "document-1",
          "company": null,
          "ratios": [],
          "comparisons": [],
          "riskSignals": [],
          "riskEvidence": [],
          "warnings": [],
          "limitations": []
        }
        """;

        var context = JsonSerializer.Deserialize<FinancialAnalysisContext>(json, JsonOptions);

        context!.Execution.Should().Be(FinancialAnalysisExecution.LegacyUnknown);
    }

    [Theory]
    [InlineData(FinancialAnalysisOperations.Ratios)]
    [InlineData(FinancialAnalysisOperations.Comparisons)]
    [InlineData(FinancialAnalysisOperations.Summary)]
    public void FromStages_Should_degrade_when_a_non_signal_stage_fails(string failedOperation)
    {
        var stages = CreateSucceededStages()
            .Select(stage => stage.Operation == failedOperation
                ? stage with { Status = FinancialAnalysisExecutionStatus.Failed }
                : stage);

        var execution = FinancialAnalysisExecution.FromStages(stages);

        execution.OverallStatus.Should().Be(FinancialAnalysisExecutionStatus.Degraded);
    }

    [Fact]
    public void FromStages_Should_fail_when_the_signals_stage_fails()
    {
        var stages = CreateSucceededStages()
            .Select(stage => stage.Operation == FinancialAnalysisOperations.Signals
                ? stage with { Status = FinancialAnalysisExecutionStatus.Failed }
                : stage);

        var execution = FinancialAnalysisExecution.FromStages(stages);

        execution.OverallStatus.Should().Be(FinancialAnalysisExecutionStatus.Failed);
    }

    [Fact]
    public void FromStages_Should_succeed_when_every_stage_succeeds()
    {
        var execution = FinancialAnalysisExecution.FromStages(CreateSucceededStages());

        execution.OverallStatus.Should().Be(FinancialAnalysisExecutionStatus.Succeeded);
        execution.Stages.Select(stage => stage.Operation)
            .Should()
            .Equal(FinancialAnalysisOperations.All);
    }

    [Fact]
    public void FromStages_Should_degrade_and_normalize_when_a_stage_is_missing_or_unknown()
    {
        var missing = FinancialAnalysisExecution.FromStages(
            CreateSucceededStages().Where(stage =>
                stage.Operation != FinancialAnalysisOperations.Summary)
        );
        var unknown = FinancialAnalysisExecution.FromStages(
            CreateSucceededStages().Select(stage =>
                stage.Operation == FinancialAnalysisOperations.Ratios
                    ? stage with { Status = FinancialAnalysisExecutionStatus.LegacyUnknown }
                    : stage)
        );

        missing.OverallStatus.Should().Be(FinancialAnalysisExecutionStatus.Degraded);
        missing.Stages.Should().ContainSingle(stage =>
            stage.Operation == FinancialAnalysisOperations.Summary &&
            stage.Status == FinancialAnalysisExecutionStatus.LegacyUnknown
        );
        unknown.OverallStatus.Should().Be(FinancialAnalysisExecutionStatus.Degraded);
    }

    [Fact]
    public void FromStages_Should_use_the_last_duplicate_stage()
    {
        var stages = CreateSucceededStages().Concat(
        [
            new FinancialAnalysisStageExecution(
                FinancialAnalysisOperations.Ratios,
                FinancialAnalysisExecutionStatus.Failed,
                10,
                FinancialAnalysisFailureCodes.PythonInvocationFailed)
        ]);

        var execution = FinancialAnalysisExecution.FromStages(stages);

        execution.OverallStatus.Should().Be(FinancialAnalysisExecutionStatus.Degraded);
        execution.Stages.Should().ContainSingle(stage =>
            stage.Operation == FinancialAnalysisOperations.Ratios &&
            stage.Status == FinancialAnalysisExecutionStatus.Failed &&
            stage.FailureCode == FinancialAnalysisFailureCodes.PythonInvocationFailed
        );
    }

    [Fact]
    public void Request_session_id_Should_be_omitted_from_all_python_json_contracts()
    {
        var sessionId = Guid.NewGuid();
        object[] requests =
        [
            new ComputeFinancialRatiosRequest([], [], sessionId),
            new ComparePeriodsRequest([], "2024A", "2025E", [], sessionId),
            new DetectFinancialRiskSignalsRequest([], [], [], SessionId: sessionId),
            new SummarizeQuantitativeEvidenceRequest([], [], [], [], SessionId: sessionId)
        ];

        foreach (var request in requests)
        {
            var json = JsonSerializer.Serialize(request, request.GetType(), JsonOptions);

            json.Should().NotContain("sessionId");
            json.Should().NotContain(sessionId.ToString());
        }
    }

    [Fact]
    public void Data_agent_result_requires_human_review_Should_round_trip_and_default_false_for_old_json()
    {
        var result = new DataAgentResult(
            HasAnomaly: true,
            Severity: "High",
            Summary: "Review required.",
            Engine: "Financial Analysis",
            Evidence: [],
            RequiresHumanReview: true
        );
        var json = JsonSerializer.Serialize(result, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<DataAgentResult>(json, JsonOptions);
        var legacy = JsonSerializer.Deserialize<DataAgentResult>(
            """
            {
              "hasAnomaly": false,
              "severity": "Low",
              "summary": "Legacy result.",
              "engine": "Legacy",
              "evidence": []
            }
            """,
            JsonOptions
        );

        json.Should().Contain("\"requiresHumanReview\":true");
        roundTripped!.RequiresHumanReview.Should().BeTrue();
        legacy!.RequiresHumanReview.Should().BeFalse();
    }

    [Fact]
    public void ComputeFinancialRatiosRequest_Should_round_trip_as_json()
    {
        var request = new ComputeFinancialRatiosRequest(
            Metrics:
            [
                CreateMetric("revenue", "2025E", 1950m),
                CreateMetric("ebitda", "2025E", 930m),
                CreateMetric("net_debt", "2025E", 1080m)
            ],
            RequestedRatios:
            [
                "ebitda_margin",
                "net_debt_to_ebitda"
            ]
        );

        var json = JsonSerializer.Serialize(request, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<ComputeFinancialRatiosRequest>(
            json,
            JsonOptions
        );

        json.Should().Contain("\"requestedRatios\"");
        roundTripped.Should().BeEquivalentTo(request);
    }

    [Fact]
    public void Tool_responses_Should_round_trip_as_json()
    {
        var ratio = new FinancialRatio(
            Name: "ebitda_margin",
            Period: "2025E",
            Value: 0.4769m,
            Unit: "ratio",
            Formula: "ebitda / revenue",
            Inputs:
            [
                "ebitda",
                "revenue"
            ],
            Interpretation: "EBITDA margin remained strong."
        );
        var comparison = new FinancialPeriodComparison(
            MetricName: "revenue",
            FromPeriod: "2024A",
            ToPeriod: "2025E",
            FromValue: 1650m,
            ToValue: 1950m,
            AbsoluteChange: 300m,
            PercentageChange: 0.1818m,
            Unit: "USD millions",
            Interpretation: "Revenue increased year over year."
        );
        var evidence = new RiskEvidenceItem(
            MetricName: "net_debt_to_ebitda",
            Period: "2025E",
            Value: 1.16m,
            Threshold: 2.5m,
            Unit: "x",
            Interpretation: "Leverage remains below risk threshold."
        );
        var signal = new FinancialRiskSignal(
            Name: "leverage_watch",
            Severity: "Low",
            Period: "2025E",
            Summary: "Leverage should be monitored.",
            Evidence: [evidence],
            Metric: "net_debt_to_ebitda",
            Value: 1.16m,
            ThresholdCode: "HIGH_NET_DEBT_TO_EBITDA",
            ThresholdOperator: ">=",
            ThresholdValue: 2.5m,
            Reason: "net_debt_to_ebitda 1.16 did not cross the configured threshold >= 2.5."
        );
        var result = new FinancialAnalysisToolResult(
            HasRiskSignals: true,
            RiskLevel: "Low",
            Summary: "Quantitative evidence was collected.",
            Engine: "Financial Analysis Contracts",
            Evidence: [evidence],
            Warnings: ["Structured metrics only."]
        );
        var responses = new
        {
            ratios = new ComputeFinancialRatiosResponse(
                Engine: "Financial Ratio Engine",
                Ratios: [ratio],
                Warnings: []
            ),
            comparisons = new ComparePeriodsResponse(
                Engine: "Period Comparison Engine",
                Comparisons: [comparison],
                Warnings: []
            ),
            signals = new DetectFinancialRiskSignalsResponse(
                Engine: "Risk Signal Engine",
                Signals: [signal],
                Result: result
            ),
            summary = new SummarizeQuantitativeEvidenceResponse(
                Engine: "Evidence Summary Engine",
                Narrative: "Revenue and EBITDA grew while leverage remained controlled.",
                Result: result
            )
        };

        var json = JsonSerializer.Serialize(responses, JsonOptions);

        json.Should().Contain("\"ratios\"");
        json.Should().Contain("\"comparisons\"");
        json.Should().Contain("\"signals\"");
        json.Should().Contain("\"summary\"");
        json.Should().Contain("\"hasRiskSignals\":true");
        json.Should().Contain("net_debt_to_ebitda");
        json.Should().Contain("\"thresholdCode\":\"HIGH_NET_DEBT_TO_EBITDA\"");
        json.Should().Contain("\"thresholdOperator\":\"\\u003E=\"");
        json.Should().Contain("\"thresholdValue\":2.5");
        json.Should().Contain("\"reason\"");
    }

    [Fact]
    public void FinancialRiskSignal_Should_deserialize_old_json_without_explainability_fields()
    {
        const string json = """
        {
          "name": "LOW_CURRENT_RATIO",
          "severity": "High",
          "period": "2025E",
          "summary": "Current ratio below threshold.",
          "evidence": []
        }
        """;

        var signal = JsonSerializer.Deserialize<FinancialRiskSignal>(
            json,
            JsonOptions
        );

        signal.Should().NotBeNull();
        signal!.Name.Should().Be("LOW_CURRENT_RATIO");
        signal.Metric.Should().BeNull();
        signal.Value.Should().BeNull();
        signal.ThresholdCode.Should().BeNull();
        signal.ThresholdOperator.Should().BeNull();
        signal.ThresholdValue.Should().BeNull();
        signal.Reason.Should().BeNull();
    }

    [Fact]
    public void Vista_energy_fixture_Should_deserialize_required_structured_metrics()
    {
        var fixture = LoadVistaFixture();
        var requiredMetricNames = new[]
        {
            "revenue",
            "gross_profit",
            "ebitda",
            "ebit",
            "net_income",
            "current_assets",
            "current_liabilities",
            "cash",
            "total_debt",
            "net_debt",
            "equity",
            "free_cash_flow",
            "capex",
            "interest_expense"
        };

        fixture.Company.Should().Be("Vista Energy");
        fixture.Periods.Should().BeEquivalentTo(["2024A", "2025E", "2026E"]);
        fixture.Metrics.Should().HaveCount(requiredMetricNames.Length * fixture.Periods.Count);

        foreach (var period in fixture.Periods)
        {
            var periodMetrics = fixture.Metrics
                .Where(metric => metric.Period == period)
                .Select(metric => metric.Name)
                .ToArray();

            periodMetrics.Should().BeEquivalentTo(requiredMetricNames);
        }

        fixture.Metrics.Should().OnlyContain(metric =>
            metric.Unit == "USD millions" &&
            !string.IsNullOrWhiteSpace(metric.Statement) &&
            metric.Value >= 0m
        );
    }

    [Fact]
    public void Vista_energy_fixture_Should_build_request_contracts()
    {
        var fixture = LoadVistaFixture();
        var ratioRequest = new ComputeFinancialRatiosRequest(
            Metrics: fixture.Metrics,
            RequestedRatios:
            [
                "gross_margin",
                "ebitda_margin",
                "net_debt_to_ebitda",
                "interest_coverage"
            ]
        );
        var compareRequest = new ComparePeriodsRequest(
            Metrics: fixture.Metrics,
            FromPeriod: "2024A",
            ToPeriod: "2025E",
            MetricNames:
            [
                "revenue",
                "ebitda",
                "free_cash_flow",
                "net_debt"
            ]
        );

        var ratioJson = JsonSerializer.Serialize(ratioRequest, JsonOptions);
        var compareJson = JsonSerializer.Serialize(compareRequest, JsonOptions);

        JsonSerializer.Deserialize<ComputeFinancialRatiosRequest>(ratioJson, JsonOptions)
            .Should()
            .BeEquivalentTo(ratioRequest);
        JsonSerializer.Deserialize<ComparePeriodsRequest>(compareJson, JsonOptions)
            .Should()
            .BeEquivalentTo(compareRequest);
    }

    private static VistaEnergySampleFixture LoadVistaFixture()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "vista_energy_sample_metrics.json"
        );
        var json = File.ReadAllText(path);

        return JsonSerializer.Deserialize<VistaEnergySampleFixture>(
            json,
            JsonOptions
        ) ?? throw new InvalidOperationException("Vista Energy fixture could not be deserialized.");
    }

    private static FinancialMetric CreateMetric(
        string name,
        string period,
        decimal value)
    {
        return new FinancialMetric(
            Name: name,
            Period: period,
            Value: value,
            Unit: "USD millions",
            Statement: "sample_statement",
            Source: "unit_test"
        );
    }

    private static IReadOnlyList<FinancialAnalysisStageExecution> CreateSucceededStages()
    {
        return FinancialAnalysisOperations.All
            .Select(operation => new FinancialAnalysisStageExecution(
                operation,
                FinancialAnalysisExecutionStatus.Succeeded,
                5))
            .ToArray();
    }

    private sealed record VistaEnergySampleFixture(
        string Company,
        string Source,
        string Currency,
        string Unit,
        IReadOnlyList<string> Periods,
        IReadOnlyList<FinancialMetric> Metrics
    );
}
