using System.Text;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsValidator : IStructuredFinancialMetricsValidator
{
    private const decimal DefaultConfidence = 0.75m;
    private const string DefaultSource = "structured_input";
    private const string UnknownUnit = "unknown";

    private static readonly HashSet<string> KnownMetricNames = new(StringComparer.Ordinal)
    {
        "revenue",
        "gross_profit",
        "operating_income",
        "ebitda",
        "ebit",
        "net_income",
        "current_assets",
        "current_liabilities",
        "cash",
        "short_term_investments",
        "receivables",
        "total_debt",
        "net_debt",
        "equity",
        "free_cash_flow",
        "capex",
        "interest_expense"
    };

    public FinancialMetricsValidationResult Validate(
        StructuredFinancialMetricsInput input)
    {
        var errors = new List<FinancialMetricsValidationIssue>();
        var warnings = new List<FinancialMetricsValidationIssue>();
        var validatedMetrics = new Dictionary<(string Name, string Period), ValidatedFinancialMetric>();

        if (input is null)
        {
            errors.Add(Error(
                "INPUT_REQUIRED",
                "Structured financial metrics input is required."
            ));

            return BuildResult(validatedMetrics, errors, warnings);
        }

        if (string.IsNullOrWhiteSpace(input.DocumentId))
        {
            errors.Add(Error(
                "DOCUMENT_ID_REQUIRED",
                "DocumentId is required."
            ));
        }

        AddDocumentWarningIfMissing(input.Company, "COMPANY_MISSING", "Company is missing.", warnings);
        AddDocumentWarningIfMissing(input.Currency, "CURRENCY_MISSING", "Currency is missing.", warnings);
        AddDocumentWarningIfMissing(input.Unit, "UNIT_MISSING", "Unit is missing.", warnings);

        if (input.Metrics is null || input.Metrics.Count == 0)
        {
            errors.Add(Error(
                "METRICS_REQUIRED",
                "At least one financial metric is required."
            ));

            return BuildResult(validatedMetrics, errors, warnings);
        }

        foreach (var metric in input.Metrics)
        {
            ValidateMetric(
                metric,
                input,
                validatedMetrics,
                errors,
                warnings
            );
        }

        return BuildResult(validatedMetrics, errors, warnings);
    }

    private static void ValidateMetric(
        StructuredFinancialMetricInput? metric,
        StructuredFinancialMetricsInput input,
        Dictionary<(string Name, string Period), ValidatedFinancialMetric> validatedMetrics,
        List<FinancialMetricsValidationIssue> errors,
        List<FinancialMetricsValidationIssue> warnings)
    {
        if (metric is null)
        {
            errors.Add(Error(
                "METRIC_REQUIRED",
                "Metric entry is required."
            ));

            return;
        }

        var name = NormalizeMetricName(metric.Name);
        var period = NormalizePeriod(metric.Period);

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(Error(
                "METRIC_NAME_REQUIRED",
                "Metric name is required.",
                metric.Name,
                period
            ));
        }

        if (string.IsNullOrWhiteSpace(period))
        {
            errors.Add(Error(
                "METRIC_PERIOD_REQUIRED",
                "Metric period is required.",
                name,
                metric.Period
            ));
        }

        if (metric.Value is null)
        {
            errors.Add(Error(
                "METRIC_VALUE_REQUIRED",
                "Metric value is required.",
                name,
                period
            ));
        }

        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(period) ||
            metric.Value is null)
        {
            return;
        }

        if (!KnownMetricNames.Contains(name))
        {
            warnings.Add(Warning(
                "UNKNOWN_METRIC_NAME",
                "Metric name is not in the initial known financial metric list.",
                name,
                period
            ));
        }

        var unit = ResolveUnit(metric, input, name, period, warnings);
        var currency = ResolveCurrency(metric, input, name, period, warnings);
        var source = ResolveSource(metric, name, period, warnings);
        var confidence = ResolveConfidence(metric, name, period, warnings);
        var sourcePage = ResolveSourcePage(metric, name, period, warnings);

        AddMetric(
            new ValidatedFinancialMetric(
                Name: name,
                Period: period,
                Value: metric.Value.Value,
                Unit: unit,
                Currency: currency,
                Source: source,
                SourcePage: sourcePage,
                Confidence: confidence
            ),
            validatedMetrics,
            warnings
        );
    }

    private static void AddMetric(
        ValidatedFinancialMetric metric,
        Dictionary<(string Name, string Period), ValidatedFinancialMetric> validatedMetrics,
        List<FinancialMetricsValidationIssue> warnings)
    {
        var key = (metric.Name, metric.Period);

        if (!validatedMetrics.TryGetValue(key, out var existing))
        {
            validatedMetrics[key] = metric;
            return;
        }

        if (metric.Confidence > existing.Confidence)
        {
            validatedMetrics[key] = metric;
            warnings.Add(Warning(
                "DUPLICATE_METRIC_REPLACED",
                "Duplicate metric replaced by a higher-confidence value.",
                metric.Name,
                metric.Period
            ));

            return;
        }

        warnings.Add(Warning(
            "DUPLICATE_METRIC_IGNORED",
            "Duplicate metric ignored because an equal or higher-confidence value already exists.",
            metric.Name,
            metric.Period
        ));
    }

    private static string ResolveUnit(
        StructuredFinancialMetricInput metric,
        StructuredFinancialMetricsInput input,
        string name,
        string period,
        List<FinancialMetricsValidationIssue> warnings)
    {
        if (!string.IsNullOrWhiteSpace(metric.Unit))
        {
            return metric.Unit.Trim();
        }

        warnings.Add(Warning(
            "METRIC_UNIT_DEFAULTED",
            "Metric unit was missing and was defaulted.",
            name,
            period
        ));

        return string.IsNullOrWhiteSpace(input.Unit)
            ? UnknownUnit
            : input.Unit.Trim();
    }

    private static string? ResolveCurrency(
        StructuredFinancialMetricInput metric,
        StructuredFinancialMetricsInput input,
        string name,
        string period,
        List<FinancialMetricsValidationIssue> warnings)
    {
        if (!string.IsNullOrWhiteSpace(metric.Currency))
        {
            return metric.Currency.Trim();
        }

        warnings.Add(Warning(
            "METRIC_CURRENCY_DEFAULTED",
            "Metric currency was missing and was defaulted from the document when available.",
            name,
            period
        ));

        return string.IsNullOrWhiteSpace(input.Currency)
            ? null
            : input.Currency.Trim();
    }

    private static string ResolveSource(
        StructuredFinancialMetricInput metric,
        string name,
        string period,
        List<FinancialMetricsValidationIssue> warnings)
    {
        if (!string.IsNullOrWhiteSpace(metric.Source))
        {
            return metric.Source.Trim();
        }

        warnings.Add(Warning(
            "METRIC_SOURCE_DEFAULTED",
            "Metric source was missing and was defaulted.",
            name,
            period
        ));

        return DefaultSource;
    }

    private static decimal ResolveConfidence(
        StructuredFinancialMetricInput metric,
        string name,
        string period,
        List<FinancialMetricsValidationIssue> warnings)
    {
        if (metric.Confidence is null)
        {
            warnings.Add(Warning(
                "METRIC_CONFIDENCE_DEFAULTED",
                "Metric confidence was missing and was defaulted.",
                name,
                period
            ));

            return DefaultConfidence;
        }

        if (metric.Confidence < 0m)
        {
            warnings.Add(Warning(
                "METRIC_CONFIDENCE_CLAMPED",
                "Metric confidence was below 0 and was clamped.",
                name,
                period
            ));

            return 0m;
        }

        if (metric.Confidence > 1m)
        {
            warnings.Add(Warning(
                "METRIC_CONFIDENCE_CLAMPED",
                "Metric confidence was above 1 and was clamped.",
                name,
                period
            ));

            return 1m;
        }

        return metric.Confidence.Value;
    }

    private static int? ResolveSourcePage(
        StructuredFinancialMetricInput metric,
        string name,
        string period,
        List<FinancialMetricsValidationIssue> warnings)
    {
        if (metric.SourcePage is null)
        {
            return null;
        }

        if (metric.SourcePage <= 0)
        {
            warnings.Add(Warning(
                "METRIC_SOURCE_PAGE_IGNORED",
                "Metric source page was not positive and was ignored.",
                name,
                period
            ));

            return null;
        }

        return metric.SourcePage;
    }

    private static string NormalizeMetricName(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var builder = new StringBuilder();
        var previousUnderscore = false;

        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character) || character == '-')
            {
                if (!previousUnderscore)
                {
                    builder.Append('_');
                    previousUnderscore = true;
                }

                continue;
            }

            builder.Append(char.ToLowerInvariant(character));
            previousUnderscore = character == '_';
        }

        return builder.ToString().Trim('_');
    }

    private static string NormalizePeriod(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Trim().ToUpperInvariant();
    }

    private static void AddDocumentWarningIfMissing(
        string? value,
        string code,
        string message,
        List<FinancialMetricsValidationIssue> warnings)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            warnings.Add(Warning(code, message));
        }
    }

    private static FinancialMetricsValidationResult BuildResult(
        Dictionary<(string Name, string Period), ValidatedFinancialMetric> validatedMetrics,
        IReadOnlyList<FinancialMetricsValidationIssue> errors,
        IReadOnlyList<FinancialMetricsValidationIssue> warnings)
    {
        return new FinancialMetricsValidationResult(
            IsValid: errors.Count == 0,
            Metrics: validatedMetrics.Values.ToArray(),
            Errors: errors,
            Warnings: warnings
        );
    }

    private static FinancialMetricsValidationIssue Error(
        string code,
        string message,
        string? metricName = null,
        string? period = null)
    {
        return new FinancialMetricsValidationIssue(
            Code: code,
            Message: message,
            MetricName: metricName,
            Period: period,
            Severity: "Error"
        );
    }

    private static FinancialMetricsValidationIssue Warning(
        string code,
        string message,
        string? metricName = null,
        string? period = null)
    {
        return new FinancialMetricsValidationIssue(
            Code: code,
            Message: message,
            MetricName: metricName,
            Period: period,
            Severity: "Warning"
        );
    }
}
