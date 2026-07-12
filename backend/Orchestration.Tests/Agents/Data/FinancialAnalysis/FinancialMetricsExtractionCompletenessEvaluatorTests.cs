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
    public void FinancialMetricsExtractionOptions_Should_have_required_contract_defaults()
    {
        var options = new FinancialMetricsExtractionOptions();

        FinancialMetricsExtractionOptions.SectionName.Should().Be("FinancialMetricsExtraction");
        options.SemanticEnrichmentEnabled.Should().BeFalse();
        options.Mode.Should().Be("ReviewOnly");
        options.DeterministicCoverageThreshold.Should().Be(0.7m);
        options.AutomaticAcceptanceConfidence.Should().Be(0.9m);
        options.MaxMarkdownCharacters.Should().Be(200_000);
        options.MaxMarkdownChunks.Should().Be(12);
        options.ConversionTimeoutSeconds.Should().Be(60);
        options.MaxWorkerMemoryBytes.Should().Be(1_073_741_824);
        options.MaxConcurrentConversions.Should().Be(2);
        options.SemanticExtractionTimeoutSeconds.Should().Be(90);
        options.MaxEvidenceExcerptCharacters.Should().Be(500);
    }

    [Fact]
    public void Evaluate_Should_require_fallback_when_currency_is_missing()
    {
        var result = CreateResult(currency: " ");

        var decision = _evaluator.Evaluate(result, new FinancialMetricsExtractionOptions());

        decision.RequiresSemanticFallback.Should().BeTrue();
        decision.ReasonCodes.Should().Equal("currency_missing");
    }

    [Fact]
    public void Evaluate_Should_add_company_missing_before_currency_and_unit_reasons()
    {
        var result = CreateResult(company: null, currency: null, unit: null);

        var decision = _evaluator.Evaluate(result, new FinancialMetricsExtractionOptions());

        decision.RequiresSemanticFallback.Should().BeTrue();
        decision.ReasonCodes.Should().StartWith(
            "company_missing",
            "currency_missing",
            "unit_missing");
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
    public void Evaluate_Should_require_ratio_inputs_when_formula_inputs_are_split_across_periods()
    {
        var metrics = CreateCompleteMetrics()
            .Where(metric => metric.Name != "gross_margin")
            .Where(metric =>
                !(metric.Name == "revenue" && metric.Period == "2024A"))
            .Where(metric =>
                !(metric.Name == "gross_profit" && metric.Period == "2025E"))
            .ToArray();
        var result = CreateResult(metrics: metrics);

        var decision = _evaluator.Evaluate(result, new FinancialMetricsExtractionOptions());

        decision.RequiresSemanticFallback.Should().BeTrue();
        decision.ReasonCodes.Should().Equal("required_ratio_inputs_missing");
    }

    [Fact]
    public void Evaluate_Should_require_ratio_inputs_when_formula_denominator_is_zero()
    {
        var metrics = CreateCompleteMetrics()
            .Where(metric => metric.Name != "gross_margin")
            .Select(metric => metric.Name == "revenue"
                ? metric with { Value = 0m }
                : metric)
            .ToArray();
        var result = CreateResult(metrics: metrics);

        var decision = _evaluator.Evaluate(result, new FinancialMetricsExtractionOptions());

        decision.RequiresSemanticFallback.Should().BeTrue();
        decision.ReasonCodes.Should().Equal("required_ratio_inputs_missing");
    }

    [Fact]
    public void Evaluate_Should_support_same_normalized_period_with_zero_numerator()
    {
        var metrics = CreateCompleteMetrics()
            .Where(metric => metric.Name != "gross_margin")
            .Where(metric =>
                !(metric.Name == "gross_profit" && metric.Period == "2025E"))
            .Select(metric =>
                metric.Name == "gross_profit" && metric.Period == "2024A"
                    ? metric with { Period = " 2024a ", Value = 0m }
                    : metric)
            .ToArray();
        var result = CreateResult(metrics: metrics);

        var decision = _evaluator.Evaluate(result, new FinancialMetricsExtractionOptions());

        decision.ReasonCodes.Should().NotContain("required_ratio_inputs_missing");
        decision.RequiresSemanticFallback.Should().BeFalse();
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
            "company_missing",
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
    public void Evaluate_Should_require_coverage_when_only_one_period_has_canonical_metrics()
    {
        var metrics = CanonicalMetrics("2024A")
            .Concat(ReportedRatioMetrics())
            .ToArray();
        var result = CreateResult(metrics: metrics);

        var decision = _evaluator.Evaluate(result, new FinancialMetricsExtractionOptions());

        decision.RequiresSemanticFallback.Should().BeTrue();
        decision.ReasonCodes.Should().Equal("metric_coverage_below_threshold");
    }

    [Fact]
    public void Evaluate_Should_require_coverage_when_invalid_result_retains_complete_input()
    {
        var result = CreateResult(isValid: false);

        var decision = _evaluator.Evaluate(result, new FinancialMetricsExtractionOptions());

        decision.RequiresSemanticFallback.Should().BeTrue();
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

    [Fact]
    public void Evaluate_Should_require_ratio_inputs_when_only_total_debt_cash_and_ebitda_are_available()
    {
        var metrics = CreateComputedRatioMetrics()
            .ToArray();
        var result = CreateResult(metrics: metrics);

        var decision = _evaluator.Evaluate(result, new FinancialMetricsExtractionOptions());

        decision.RequiresSemanticFallback.Should().BeTrue();
        decision.ReasonCodes.Should().Equal("required_ratio_inputs_missing");
    }

    [Fact]
    public void Evaluate_Should_support_net_debt_and_ebitda_for_net_debt_ratio()
    {
        var metrics = CreateComputedRatioMetrics()
            .Where(metric => metric.Name is not ("cash" or "total_debt"))
            .Concat(
            [
                Metric("net_debt", "2024A"),
                Metric("debt_to_equity", "2024A")
            ])
            .ToArray();
        var result = CreateResult(metrics: metrics);

        var decision = _evaluator.Evaluate(result, new FinancialMetricsExtractionOptions());

        decision.RequiresSemanticFallback.Should().BeFalse();
        decision.ReasonCodes.Should().BeEmpty();
    }

    private static IEnumerable<StructuredFinancialMetricInput> CreateComputedRatioMetrics()
    {
        return CanonicalMetrics("2024A")
            .Concat(CanonicalMetrics("2025E"))
            .Concat(
            [
                Metric("current_assets", "2024A"),
                Metric("current_liabilities", "2024A"),
                Metric("receivables", "2024A"),
                Metric("ebit", "2024A"),
                Metric("interest_expense", "2024A"),
                Metric("capex", "2024A")
            ]);
    }

    private static StructuredFinancialMetricsPdfExtractionResult CreateResult(
        string? company = "Vista Energy",
        string? currency = "USD",
        string? unit = "USD_thousand",
        IReadOnlyList<StructuredFinancialMetricInput>? metrics = null,
        bool isValid = true)
    {
        return new StructuredFinancialMetricsPdfExtractionResult(
            IsValid: isValid,
            Input: new StructuredFinancialMetricsInput(
                DocumentId: "report-1",
                Company: company,
                Currency: currency,
                Unit: unit,
                Metrics: metrics ?? CreateCompleteMetrics(),
                ReportSummary: TestReportSummary.Input),
            Errors: [],
            Warnings: [],
            UsedOcr: false);
    }

    private static IReadOnlyList<StructuredFinancialMetricInput> CreateCompleteMetrics()
    {
        return CanonicalMetrics("2024A")
            .Concat(CanonicalMetrics("2025E"))
            .Concat(ReportedRatioMetrics())
            .ToArray();
    }

    private static IEnumerable<StructuredFinancialMetricInput> CanonicalMetrics(
        string period)
    {
        return CanonicalBaseMetrics.Select(name => Metric(name, period));
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
