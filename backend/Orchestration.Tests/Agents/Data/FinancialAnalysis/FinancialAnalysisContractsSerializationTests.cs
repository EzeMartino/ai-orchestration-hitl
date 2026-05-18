using System.Text.Json;
using FluentAssertions;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public class FinancialAnalysisContractsSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
            Evidence: [evidence]
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

    private sealed record VistaEnergySampleFixture(
        string Company,
        string Source,
        string Currency,
        string Unit,
        IReadOnlyList<string> Periods,
        IReadOnlyList<FinancialMetric> Metrics
    );
}
