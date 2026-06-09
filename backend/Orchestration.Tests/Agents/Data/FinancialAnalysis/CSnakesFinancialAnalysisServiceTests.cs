using System.Text.Json;
using FluentAssertions;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.FinancialAnalysis.Thresholds;
using Orchestration.Tests.Agents.Data;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

[Collection(PythonAgentTestCollection.CollectionName)]
public sealed class CSnakesFinancialAnalysisServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly PythonAgentTestFixture _fixture;

    public CSnakesFinancialAnalysisServiceTests(PythonAgentTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ComputeFinancialRatiosAsync_Should_compute_vista_core_ratios()
    {
        var service = _fixture.GetRequiredService<IPythonFinancialAnalysisService>();
        var request = new ComputeFinancialRatiosRequest(
            Metrics: LoadVistaMetrics(),
            RequestedRatios:
            [
                "gross_margin",
                "ebitda_margin",
                "current_ratio",
                "net_debt_to_ebitda"
            ]
        );

        var response = await service.ComputeFinancialRatiosAsync(
            request,
            CancellationToken.None
        );

        response.Engine.Should().Be("Python/CSnakes Financial Analysis");
        response.Warnings.Should().BeEmpty();
        response.Ratios.Should().Contain(ratio =>
            ratio.Name == "gross_margin" &&
            ratio.Period == "2024A" &&
            ratio.Value > 0m
        );
        response.Ratios.Should().Contain(ratio =>
            ratio.Name == "ebitda_margin" &&
            ratio.Period == "2025E" &&
            ratio.Value > 0m
        );
        response.Ratios.Should().Contain(ratio =>
            ratio.Name == "current_ratio" &&
            ratio.Period == "2026E" &&
            ratio.Value > 0m
        );
        response.Ratios.Should().Contain(ratio =>
            ratio.Name == "net_debt_to_ebitda" &&
            ratio.Period == "2025E" &&
            ratio.Value > 0m
        );
    }

    [Fact]
    public async Task ComputeFinancialRatiosAsync_Should_return_warnings_when_inputs_are_missing()
    {
        var service = _fixture.GetRequiredService<IPythonFinancialAnalysisService>();
        var request = new ComputeFinancialRatiosRequest(
            Metrics: [CreateMetric("revenue", "2024A", 100m)],
            RequestedRatios: ["gross_margin"]
        );

        var response = await service.ComputeFinancialRatiosAsync(
            request,
            CancellationToken.None
        );

        response.Ratios.Should().BeEmpty();
        response.Warnings.Should().Contain(warning =>
            warning.Contains("Falta el numerador", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task ComputeFinancialRatiosAsync_Should_preserve_reported_ratio_source()
    {
        var service = _fixture.GetRequiredService<IPythonFinancialAnalysisService>();
        var reportedGrossMargin = CreateMetric("gross_margin", "2024A", 0.415m);
        var request = new ComputeFinancialRatiosRequest(
            Metrics: [reportedGrossMargin],
            RequestedRatios: ["gross_margin"]
        );

        var response = await service.ComputeFinancialRatiosAsync(
            request,
            CancellationToken.None
        );

        var ratio = response.Ratios.Should().ContainSingle().Subject;
        ratio.Name.Should().Be("gross_margin");
        ratio.Source.Should().Be("reported");
        ratio.Formula.Should().Be("reported");
        ratio.Inputs.Should().Equal("gross_margin");
    }

    [Fact]
    public async Task ComparePeriodsAsync_Should_compute_percentage_change()
    {
        var service = _fixture.GetRequiredService<IPythonFinancialAnalysisService>();
        var request = new ComparePeriodsRequest(
            Metrics: LoadVistaMetrics(),
            FromPeriod: "2024A",
            ToPeriod: "2025E",
            MetricNames: ["revenue"]
        );

        var response = await service.ComparePeriodsAsync(
            request,
            CancellationToken.None
        );

        var comparison = response.Comparisons.Should().ContainSingle().Subject;

        comparison.MetricName.Should().Be("revenue");
        comparison.FromPeriod.Should().Be("2024A");
        comparison.ToPeriod.Should().Be("2025E");
        comparison.PercentageChange.Should().NotBeNull();
        comparison.Interpretation.Should().Contain("revenue");
    }

    [Fact]
    public async Task ComparePeriodsAsync_Should_detect_high_revenue_drop()
    {
        var service = _fixture.GetRequiredService<IPythonFinancialAnalysisService>();
        var request = new ComparePeriodsRequest(
            Metrics:
            [
                CreateMetric("revenue", "2024A", 100m),
                CreateMetric("revenue", "2025E", 70m)
            ],
            FromPeriod: "2024A",
            ToPeriod: "2025E",
            MetricNames: ["revenue"]
        );

        var response = await service.ComparePeriodsAsync(
            request,
            CancellationToken.None
        );

        response.Comparisons.Should().ContainSingle()
            .Which.Interpretation.Should().Contain("severidad=High");
    }

    [Fact]
    public async Task ComparePeriodsAsync_Should_handle_missing_period_safely()
    {
        var service = _fixture.GetRequiredService<IPythonFinancialAnalysisService>();
        var request = new ComparePeriodsRequest(
            Metrics: LoadVistaMetrics(),
            FromPeriod: "2023A",
            ToPeriod: "2025E",
            MetricNames: ["revenue"]
        );

        var response = await service.ComparePeriodsAsync(
            request,
            CancellationToken.None
        );

        response.Comparisons.Should().BeEmpty();
        response.Warnings.Should().Contain(warning =>
            warning.Contains("Falta la métrica revenue", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task DetectFinancialRiskSignalsAsync_Should_flag_liquidity_and_leverage_signals()
    {
        var service = _fixture.GetRequiredService<IPythonFinancialAnalysisService>();
        var metrics = CreateRiskyMetrics();
        var ratios = await service.ComputeFinancialRatiosAsync(
            new ComputeFinancialRatiosRequest(
                Metrics: metrics,
                RequestedRatios:
                [
                    "current_ratio",
                    "quick_ratio",
                    "net_debt_to_ebitda"
                ]
            ),
            CancellationToken.None
        );
        var request = new DetectFinancialRiskSignalsRequest(
            Metrics: metrics,
            Ratios: ratios.Ratios,
            Comparisons: []
        );

        var response = await service.DetectFinancialRiskSignalsAsync(
            request,
            CancellationToken.None
        );

        response.Result.HasRiskSignals.Should().BeTrue();
        response.Result.RiskLevel.Should().Be("High");
        response.Signals.Should().Contain(signal => signal.Name == "LOW_CURRENT_RATIO");
        response.Signals.Should().Contain(signal => signal.Name == "HIGH_NET_DEBT_TO_EBITDA");
        response.Signals.Should().Contain(signal => signal.Name == "NEGATIVE_FREE_CASH_FLOW");
    }

    [Fact]
    public async Task DetectFinancialRiskSignalsAsync_Should_return_no_high_risk_signals_for_healthy_metrics()
    {
        var service = _fixture.GetRequiredService<IPythonFinancialAnalysisService>();
        var metrics = CreateHealthyMetrics();
        var ratios = await service.ComputeFinancialRatiosAsync(
            new ComputeFinancialRatiosRequest(
                Metrics: metrics,
                RequestedRatios:
                [
                    "current_ratio",
                    "quick_ratio",
                    "net_debt_to_ebitda",
                    "debt_to_equity",
                    "interest_coverage",
                    "ebitda_margin"
                ]
            ),
            CancellationToken.None
        );
        var request = new DetectFinancialRiskSignalsRequest(
            Metrics: metrics,
            Ratios: ratios.Ratios,
            Comparisons: []
        );

        var response = await service.DetectFinancialRiskSignalsAsync(
            request,
            CancellationToken.None
        );

        response.Signals.Should().BeEmpty();
        response.Result.HasRiskSignals.Should().BeFalse();
    }

    [Fact]
    public async Task DetectFinancialRiskSignalsAsync_Should_respect_custom_thresholds_passed_from_CSharp()
    {
        var service = _fixture.GetRequiredService<IPythonFinancialAnalysisService>();
        var metrics = CreateHealthyMetrics();
        var ratios = new[]
        {
            new FinancialRatio(
                Name: "net_debt_to_ebitda",
                Period: "2025A",
                Value: 0.625m,
                Unit: "x",
                Formula: "net_debt / ebitda",
                Inputs: ["net_debt", "ebitda"],
                Interpretation: "Healthy leverage ratio."
            )
        };

        // Scenario 1: Strict threshold is high (2.5), so net_debt_to_ebitda (0.625) does not trigger it
        var strictThresholds = new[]
        {
            new FinancialRiskThreshold("HIGH_NET_DEBT_TO_EBITDA", "net_debt_to_ebitda", ">=", 2.5m, "High", "Strict threshold is 2.5")
        };
        var strictRequest = new DetectFinancialRiskSignalsRequest(
            Metrics: metrics,
            Ratios: ratios,
            Comparisons: [],
            ThresholdProfileName: "strict",
            Thresholds: strictThresholds
        );

        var strictResponse = await service.DetectFinancialRiskSignalsAsync(
            strictRequest,
            CancellationToken.None
        );

        strictResponse.Signals.Should().NotContain(s => s.Name == "HIGH_NET_DEBT_TO_EBITDA");

        // Scenario 2: Demo/Custom threshold is very sensitive (0.5), so net_debt_to_ebitda (0.625) should trigger it
        var demoThresholds = new[]
        {
            new FinancialRiskThreshold("HIGH_NET_DEBT_TO_EBITDA", "net_debt_to_ebitda", ">=", 0.5m, "High", "Demo threshold is 0.5")
        };
        var demoRequest = new DetectFinancialRiskSignalsRequest(
            Metrics: metrics,
            Ratios: ratios,
            Comparisons: [],
            ThresholdProfileName: "demo",
            Thresholds: demoThresholds
        );

        var demoResponse = await service.DetectFinancialRiskSignalsAsync(
            demoRequest,
            CancellationToken.None
        );

        demoResponse.Signals.Should().Contain(s => s.Name == "HIGH_NET_DEBT_TO_EBITDA");
        var triggeredSignal = demoResponse.Signals.First(s => s.Name == "HIGH_NET_DEBT_TO_EBITDA");
        triggeredSignal.Evidence.Should().ContainSingle(e => e.MetricName == "net_debt_to_ebitda" && e.Threshold == 0.5m && e.Value == 0.625m);
        triggeredSignal.Metric.Should().Be("net_debt_to_ebitda");
        triggeredSignal.Value.Should().Be(0.625m);
        triggeredSignal.ThresholdCode.Should().Be("HIGH_NET_DEBT_TO_EBITDA");
        triggeredSignal.ThresholdOperator.Should().Be(">=");
        triggeredSignal.ThresholdValue.Should().Be(0.5m);
        triggeredSignal.Reason.Should().Contain("net_debt_to_ebitda");
        triggeredSignal.Reason.Should().Contain(">= 0.5");
    }

    [Fact]
    public async Task SummarizeQuantitativeEvidenceAsync_Should_return_summary_order_high_severity_first_and_respect_max_items()
    {
        var service = _fixture.GetRequiredService<IPythonFinancialAnalysisService>();
        var highEvidence = new RiskEvidenceItem(
            MetricName: "net_debt_to_ebitda",
            Period: "2025E",
            Value: 3.75m,
            Threshold: 3.0m,
            Unit: "x",
            Interpretation: "Leverage threshold exceeded."
        );
        var mediumEvidence = new RiskEvidenceItem(
            MetricName: "current_ratio",
            Period: "2025E",
            Value: 0.78m,
            Threshold: 1.0m,
            Unit: "x",
            Interpretation: "Possible liquidity issue."
        );
        var request = new SummarizeQuantitativeEvidenceRequest(
            Metrics: [],
            Ratios: [],
            Comparisons: [],
            Signals:
            [
                new FinancialRiskSignal(
                    Name: "LOW_CURRENT_RATIO",
                    Severity: "Medium",
                    Period: "2025E",
                    Summary: "Liquidity requires review.",
                    Evidence: [mediumEvidence]
                ),
                new FinancialRiskSignal(
                    Name: "HIGH_NET_DEBT_TO_EBITDA",
                    Severity: "High",
                    Period: "2025E",
                    Summary: "Leverage requires review.",
                    Evidence: [highEvidence]
                )
            ],
            MaxItems: 1
        );

        var response = await service.SummarizeQuantitativeEvidenceAsync(
            request,
            CancellationToken.None
        );

        response.Narrative.Should().Contain("severidad alta");
        response.Result.Evidence.Should().ContainSingle()
            .Which.MetricName.Should().Be("net_debt_to_ebitda");
        response.Result.RiskLevel.Should().Be("High");
    }

    private static IReadOnlyList<FinancialMetric> LoadVistaMetrics()
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
        )?.Metrics ?? throw new InvalidOperationException("Vista fixture could not be loaded.");
    }

    private static IReadOnlyList<FinancialMetric> CreateRiskyMetrics()
    {
        return
        [
            CreateMetric("revenue", "2024A", 100m),
            CreateMetric("ebitda", "2024A", 30m),
            CreateMetric("free_cash_flow", "2024A", 10m),
            CreateMetric("revenue", "2025E", 80m),
            CreateMetric("ebitda", "2025E", 12m),
            CreateMetric("current_assets", "2025E", 35m),
            CreateMetric("current_liabilities", "2025E", 45m),
            CreateMetric("cash", "2025E", 10m),
            CreateMetric("net_debt", "2025E", 45m),
            CreateMetric("free_cash_flow", "2025E", -5m)
        ];
    }

    private static IReadOnlyList<FinancialMetric> CreateHealthyMetrics()
    {
        return
        [
            CreateMetric("revenue", "2024A", 100m),
            CreateMetric("ebitda", "2024A", 35m),
            CreateMetric("ebit", "2024A", 28m),
            CreateMetric("current_assets", "2024A", 120m),
            CreateMetric("current_liabilities", "2024A", 60m),
            CreateMetric("cash", "2024A", 55m),
            CreateMetric("total_debt", "2024A", 60m),
            CreateMetric("net_debt", "2024A", 30m),
            CreateMetric("equity", "2024A", 150m),
            CreateMetric("free_cash_flow", "2024A", 20m),
            CreateMetric("interest_expense", "2024A", 8m),
            CreateMetric("revenue", "2025A", 112m),
            CreateMetric("ebitda", "2025A", 40m),
            CreateMetric("ebit", "2025A", 32m),
            CreateMetric("current_assets", "2025A", 140m),
            CreateMetric("current_liabilities", "2025A", 65m),
            CreateMetric("cash", "2025A", 60m),
            CreateMetric("total_debt", "2025A", 58m),
            CreateMetric("net_debt", "2025A", 25m),
            CreateMetric("equity", "2025A", 165m),
            CreateMetric("free_cash_flow", "2025A", 24m),
            CreateMetric("interest_expense", "2025A", 8m)
        ];
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
            Statement: "unit_test",
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
