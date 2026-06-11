using FluentAssertions;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class FinancialMetricsExtractionCompletenessEvaluatorTests
{
    private static readonly string[] CanonicalBaseMetrics =
    [
        "revenue",
        "gross_profit",
        "ebitda",
        "net_income",
        "cash",
        "total_debt",
        "equity",
        "free_cash_flow"
    ];

    private readonly FinancialMetricsExtractionCompletenessEvaluator _evaluator = new();

    [Fact]
    public void Evaluate_Should_require_fallback_when_currency_is_missing()
    {
        var result = CreateResult(currency: " ");

        var decision = _evaluator.Evaluate(result, new FinancialMetricsExtractionOptions());

        decision.RequiresSemanticFallback.Should().BeTrue();
        decision.ReasonCodes.Should().Equal("currency_missing");
    }

    [Fact]
    public void Evaluate_Should_require_fallback_when_unit_is_missing()
    {
        var result = CreateResult(unit: null);

        var decision = _evaluator.Evaluate(result, new FinancialMetricsExtractionOptions());

        decision.RequiresSemanticFallback.Should().BeTrue();
        decision.ReasonCodes.Should().Equal("unit_missing");
    }

    [Fact]
    public void Evaluate_Should_require_fallback_when_required_ratio_inputs_are_missing()
    {
        var metrics = CreateCompleteMetrics()
            .Where(metric => metric.Name != "interest_coverage")
            .ToArray();
        var result = CreateResult(metrics: metrics);

        var decision = _evaluator.Evaluate(result, new FinancialMetricsExtractionOptions());

        decision.RequiresSemanticFallback.Should().BeTrue();
        decision.ReasonCodes.Should().Equal("required_ratio_inputs_missing");
    }

    [Fact]
    public void Evaluate_Should_require_fallback_when_metric_coverage_is_below_threshold()
    {
        var metrics = ReportedRatioMetrics()
            .Concat(
            [
                Metric("revenue", "2024A"),
                Metric("gross_profit", "2025E")
            ])
            .ToArray();
        var result = CreateResult(metrics: metrics);

        var decision = _evaluator.Evaluate(result, new FinancialMetricsExtractionOptions());

        decision.RequiresSemanticFallback.Should().BeTrue();
        decision.ReasonCodes.Should().Equal("metric_coverage_below_threshold");
    }

    [Fact]
    public void Evaluate_Should_skip_fallback_for_complete_multi_period_result()
    {
        var result = CreateResult();

        var decision = _evaluator.Evaluate(result, new FinancialMetricsExtractionOptions());

        decision.RequiresSemanticFallback.Should().BeFalse();
        decision.ReasonCodes.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_Should_use_stable_deduplicated_reason_order_independent_of_enrichment_setting()
    {
        var result = new StructuredFinancialMetricsPdfExtractionResult(
            IsValid: false,
            Input: null,
            Errors: [],
            Warnings: [],
            UsedOcr: false);

        var decision = _evaluator.Evaluate(
            result,
            new FinancialMetricsExtractionOptions
            {
                SemanticEnrichmentEnabled = true
            });

        decision.RequiresSemanticFallback.Should().BeTrue();
        decision.ReasonCodes.Should().Equal(
            "currency_missing",
            "unit_missing",
            "required_ratio_inputs_missing",
            "metric_coverage_below_threshold");
    }

    [Fact]
    public void Evaluate_Should_treat_fewer_than_two_nonblank_periods_as_low_coverage()
    {
        var metrics = CreateCompleteMetrics()
            .Select(metric => metric with { Period = "2024A" })
            .ToArray();
        var result = CreateResult(metrics: metrics);

        var decision = _evaluator.Evaluate(result, new FinancialMetricsExtractionOptions());

        decision.ReasonCodes.Should().Equal("metric_coverage_below_threshold");
    }

    [Fact]
    public void Evaluate_Should_clamp_coverage_threshold_to_one()
    {
        var metrics = CreateCompleteMetrics()
            .Where(metric => metric.Name != "free_cash_flow")
            .Concat([Metric("fcf_margin", "2024A")])
            .ToArray();
        var result = CreateResult(metrics: metrics);

        var decision = _evaluator.Evaluate(
            result,
            new FinancialMetricsExtractionOptions
            {
                DeterministicCoverageThreshold = 2m
            });

        decision.ReasonCodes.Should().Equal("metric_coverage_below_threshold");
    }

    [Fact]
    public void Evaluate_Should_clamp_coverage_threshold_to_zero()
    {
        var result = CreateResult(metrics: ReportedRatioMetrics());

        var decision = _evaluator.Evaluate(
            result,
            new FinancialMetricsExtractionOptions
            {
                DeterministicCoverageThreshold = -1m
            });

        decision.RequiresSemanticFallback.Should().BeFalse();
        decision.ReasonCodes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Evaluate_Should_support_computed_ratio_input_alternatives(
        bool useDirectNetDebt)
    {
        var canonicalMetrics = CanonicalBaseMetrics
            .Where(name => !useDirectNetDebt || name is not ("cash" or "total_debt"))
            .Select(name => Metric(name, "2024A"));
        var metrics = canonicalMetrics
            .Concat(
            [
                Metric("revenue", "2025E"),
                Metric("current_assets", "2024A"),
                Metric("current_liabilities", "2024A"),
                Metric("receivables", "2024A"),
                Metric("ebit", "2024A"),
                Metric("interest_expense", "2024A"),
                Metric("capex", "2024A")
            ])
            .Concat(useDirectNetDebt
                ?
                [
                    Metric("net_debt", "2024A"),
                    Metric("debt_to_equity", "2024A")
                ]
                : [])
            .ToArray();
        var result = CreateResult(metrics: metrics);

        var decision = _evaluator.Evaluate(result, new FinancialMetricsExtractionOptions());

        decision.RequiresSemanticFallback.Should().BeFalse();
        decision.ReasonCodes.Should().BeEmpty();
    }

    private static StructuredFinancialMetricsPdfExtractionResult CreateResult(
        string? currency = "USD",
        string? unit = "USD_thousand",
        IReadOnlyList<StructuredFinancialMetricInput>? metrics = null)
    {
        return new StructuredFinancialMetricsPdfExtractionResult(
            IsValid: true,
            Input: new StructuredFinancialMetricsInput(
                DocumentId: "report-1",
                Company: "Vista Energy",
                Currency: currency,
                Unit: unit,
                Metrics: metrics ?? CreateCompleteMetrics()),
            Errors: [],
            Warnings: [],
            UsedOcr: false);
    }

    private static IReadOnlyList<StructuredFinancialMetricInput> CreateCompleteMetrics()
    {
        return CanonicalBaseMetrics
            .Select(name => Metric(name, "2024A"))
            .Concat(ReportedRatioMetrics())
            .Concat([Metric("revenue", "2025E")])
            .ToArray();
    }

    private static IReadOnlyList<StructuredFinancialMetricInput> ReportedRatioMetrics()
    {
        var requestedRatios = new[]
        {
            "gross_margin",
            "ebitda_margin",
            "net_margin",
            "current_ratio",
            "quick_ratio",
            "debt_to_equity",
            "net_debt_to_ebitda",
            "interest_coverage",
            "fcf_margin",
            "capex_to_revenue"
        };

        return requestedRatios
            .Select((name, index) =>
                Metric(name, index == requestedRatios.Length - 1 ? "2025E" : "2024A"))
            .ToArray();
    }

    private static StructuredFinancialMetricInput Metric(
        string name,
        string period)
    {
        return new StructuredFinancialMetricInput(
            Name: name,
            Period: period,
            Value: 1m,
            Unit: "USD_thousand",
            Currency: "USD",
            Source: "pdf_extraction",
            SourcePage: 1,
            Confidence: 0.9m);
    }
}
