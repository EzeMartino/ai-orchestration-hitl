namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public sealed class FinancialMetricsExtractionCompletenessEvaluator
    : IFinancialMetricsExtractionCompletenessEvaluator
{
    private const string CurrencyMissing = "currency_missing";
    private const string UnitMissing = "unit_missing";
    private const string RequiredRatioInputsMissing = "required_ratio_inputs_missing";
    private const string MetricCoverageBelowThreshold = "metric_coverage_below_threshold";

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

    private static readonly RatioRequirement[] RequiredRatios =
    [
        new("gross_margin", [["gross_profit", "revenue"]]),
        new("ebitda_margin", [["ebitda", "revenue"]]),
        new("net_margin", [["net_income", "revenue"]]),
        new("current_ratio", [["current_assets", "current_liabilities"]]),
        new(
            "quick_ratio",
            [
                ["cash", "current_liabilities"],
                ["short_term_investments", "current_liabilities"],
                ["receivables", "current_liabilities"]
            ]),
        new("debt_to_equity", [["total_debt", "equity"]]),
        new("net_debt_to_ebitda", [["net_debt", "ebitda"]]),
        new("interest_coverage", [["ebit", "interest_expense"]]),
        new("fcf_margin", [["free_cash_flow", "revenue"]]),
        new("capex_to_revenue", [["capex", "revenue"]])
    ];

    public FinancialMetricsExtractionDecision Evaluate(
        StructuredFinancialMetricsPdfExtractionResult result,
        FinancialMetricsExtractionOptions options)
    {
        var reasons = new List<string>();
        var input = result.Input;

        if (string.IsNullOrWhiteSpace(input?.Currency))
        {
            reasons.Add(CurrencyMissing);
        }

        if (string.IsNullOrWhiteSpace(input?.Unit))
        {
            reasons.Add(UnitMissing);
        }

        var availableMetrics = input?.Metrics
            .Where(metric => metric.Value.HasValue && !string.IsNullOrWhiteSpace(metric.Name))
            .Select(metric => metric.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];

        if (RequiredRatios.Any(requirement => !requirement.CanBeSupportedBy(availableMetrics)))
        {
            reasons.Add(RequiredRatioInputsMissing);
        }

        var threshold = Math.Clamp(options.DeterministicCoverageThreshold, 0m, 1m);
        var coverage = CanonicalBaseMetrics.Count(availableMetrics.Contains)
            / (decimal)CanonicalBaseMetrics.Length;
        var distinctPeriods = input?.Metrics
            .Where(metric => metric.Value.HasValue && !string.IsNullOrWhiteSpace(metric.Period))
            .Select(metric => metric.Period.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count()
            ?? 0;

        if (coverage < threshold || distinctPeriods < 2)
        {
            reasons.Add(MetricCoverageBelowThreshold);
        }

        return new FinancialMetricsExtractionDecision(
            RequiresSemanticFallback: reasons.Count > 0,
            ReasonCodes: reasons);
    }

    private sealed record RatioRequirement(
        string ReportedMetricName,
        IReadOnlyList<IReadOnlyList<string>> InputAlternatives)
    {
        public bool CanBeSupportedBy(IReadOnlySet<string> availableMetrics)
        {
            return availableMetrics.Contains(ReportedMetricName)
                || InputAlternatives.Any(alternative =>
                    alternative.All(availableMetrics.Contains));
        }
    }
}
