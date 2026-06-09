using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsTextParser
    : IStructuredFinancialMetricsTextParser
{
    private static readonly IReadOnlyList<(string Alias, string Name)> MetricAliases =
    [
        ("Capital Expenditures", "capex"),
        ("Deuda financiera total", "total_debt"),
        ("Short Term Investments", "short_term_investments"),
        ("Current Liabilities", "current_liabilities"),
        ("Flujo de caja libre", "free_cash_flow"),
        ("Operating Income", "operating_income"),
        ("Current Assets", "current_assets"),
        ("Interest Expense", "interest_expense"),
        ("Free Cash Flow", "free_cash_flow"),
        ("Resultado operativo", "operating_income"),
        ("Pasivo corriente", "current_liabilities"),
        ("Activo corriente", "current_assets"),
        ("Ventas netas", "revenue"),
        ("Ganancia bruta", "gross_profit"),
        ("Patrimonio neto", "equity"),
        ("Deuda neta", "net_debt"),
        ("Margen EBITDA", "ebitda_margin"),
        ("Margen bruto", "gross_margin"),
        ("Margen neto", "net_margin"),
        ("Gross Profit", "gross_profit"),
        ("Gross Margin", "gross_margin"),
        ("EBITDA Margin", "ebitda_margin"),
        ("Net Margin", "net_margin"),
        ("Total Debt", "total_debt"),
        ("Net Income", "net_income"),
        ("Receivables", "receivables"),
        ("Inventory", "inventory"),
        ("Ingresos", "revenue"),
        ("Ventas", "revenue"),
        ("Revenue", "revenue"),
        ("Sales", "revenue"),
        ("EBITDA", "ebitda"),
        ("EBIT", "ebit"),
        ("Net Debt", "net_debt"),
        ("Equity", "equity"),
        ("Shares", "shares"),
        ("Cash", "cash"),
        ("Capex", "capex"),
        ("FCF", "free_cash_flow")
    ];

    private static readonly Regex PeriodRegex = new(
        @"\b(?:FY)?(?<year>20\d{2})(?<suffix>[AE])?\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex NumberRegex = new(
        @"(?<!\w)\(?-?\d[\d.,]*(?:[.,]\d+)?%?\)?(?!\w)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private static readonly Regex InlinePeriodValueRegex = new(
        @"\b(?:FY)?(?<year>20\d{2})(?<suffix>[AE])?\b\s+(?<value>\(?-?\d[\d.,]*(?:[.,]\d+)?%?\)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    public StructuredFinancialMetricsPdfExtractionResult Parse(
        StructuredFinancialMetricsTextParseRequest request)
    {
        var usedOcr = string.Equals(request.Source, "pdf_ocr", StringComparison.OrdinalIgnoreCase);
        var warnings = usedOcr
            ? new List<FinancialMetricsValidationIssue>
            {
                Warning(
                    "PDF_OCR_USED",
                    "El texto del PDF fue extraído utilizando OCR (Reconocimiento Óptico de Caracteres)."
                )
            }
            : [];
        var metrics = new Dictionary<(string Name, string Period), StructuredFinancialMetricInput>();

        foreach (var page in request.Pages)
        {
            ParsePage(request, page, metrics);
        }

        if (metrics.Count == 0)
        {
            return new StructuredFinancialMetricsPdfExtractionResult(
                IsValid: false,
                Input: null,
                Errors:
                [
                    Error(
                        "PDF_METRICS_NOT_FOUND",
                        "No se encontraron métricas financieras soportadas en el texto extraído del PDF."
                    )
                ],
                Warnings: warnings,
                UsedOcr: usedOcr
            );
        }

        return new StructuredFinancialMetricsPdfExtractionResult(
            IsValid: true,
            Input: new StructuredFinancialMetricsInput(
                DocumentId: request.DocumentId,
                Company: request.Company,
                Currency: request.Currency,
                Unit: request.Unit,
                Metrics: metrics.Values.ToList()
            ),
            Errors: [],
            Warnings: warnings,
            UsedOcr: usedOcr
        );
    }

    private static void ParsePage(
        StructuredFinancialMetricsTextParseRequest request,
        StructuredFinancialMetricsExtractedPage page,
        Dictionary<(string Name, string Period), StructuredFinancialMetricInput> metrics)
    {
        var periods = Array.Empty<string>();
        var confidence = page.OcrConfidence ?? 0.8m;

        foreach (var line in SplitLines(page.Text))
        {
            var trimmedLine = line.TrimStart();
            var linePeriods = ExtractPeriods(trimmedLine);

            if (linePeriods.Count > 0 && LooksLikePeriodHeader(trimmedLine))
            {
                periods = linePeriods.ToArray();
                continue;
            }

            if (periods.Length == 0 && linePeriods.Count == 0)
            {
                continue;
            }

            var aliasMatch = MatchAlias(trimmedLine);

            if (aliasMatch is null)
            {
                continue;
            }

            var valuesText = trimmedLine[aliasMatch.Value.Alias.Length..];
            var metricValues = linePeriods.Count > 0
                ? ExtractInlinePeriodValues(valuesText)
                : PairPeriodsAndValues(periods, ExtractValues(valuesText));

            foreach (var metricValue in metricValues)
            {
                var metric = new StructuredFinancialMetricInput(
                    Name: aliasMatch.Value.Name,
                    Period: metricValue.Period,
                    Value: metricValue.Value,
                    Unit: request.Unit,
                    Currency: request.Currency,
                    Source: request.Source,
                    SourcePage: page.PageNumber,
                    Confidence: confidence
                );

                UpsertMetric(metrics, metric);
            }
        }
    }

    private static IReadOnlyList<string> SplitLines(
        string text)
    {
        return text.Split(
                ["\r\n", "\n", "\r"],
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
            );
    }

    private static IReadOnlyList<string> ExtractPeriods(
        string line)
    {
        return PeriodRegex.Matches(line)
            .Select(match => NormalizePeriod(
                match.Groups["year"].Value,
                match.Groups["suffix"].Value
            ))
            .ToArray();
    }

    private static bool LooksLikePeriodHeader(
        string line)
    {
        return line.Contains("Metric", StringComparison.OrdinalIgnoreCase)
            || !MetricAliases.Any(alias =>
                line.StartsWith(alias.Alias, StringComparison.OrdinalIgnoreCase)
            );
    }

    private static (string Alias, string Name)? MatchAlias(
        string line)
    {
        var normalizedLine = NormalizeForMatch(line);

        foreach (var alias in MetricAliases.OrderByDescending(alias => alias.Alias.Length))
        {
            var normalizedAlias = NormalizeForMatch(alias.Alias);

            if (StartsWithAlias(normalizedLine, normalizedAlias))
            {
                return alias;
            }
        }

        return null;
    }

    private static IReadOnlyList<decimal> ExtractValues(
        string text)
    {
        return NumberRegex.Matches(text)
            .Select(match => ParseValue(match.Value))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
    }

    private static IReadOnlyList<(string Period, decimal Value)> ExtractInlinePeriodValues(
        string text)
    {
        return InlinePeriodValueRegex.Matches(text)
            .Select(match => (
                Period: NormalizePeriod(
                    match.Groups["year"].Value,
                    match.Groups["suffix"].Value
                ),
                Value: ParseValue(match.Groups["value"].Value)
            ))
            .Where(item => item.Value.HasValue)
            .Select(item => (item.Period, item.Value!.Value))
            .ToArray();
    }

    private static IReadOnlyList<(string Period, decimal Value)> PairPeriodsAndValues(
        IReadOnlyList<string> periods,
        IReadOnlyList<decimal> values)
    {
        var count = Math.Min(periods.Count, values.Count);
        var pairs = new List<(string Period, decimal Value)>(count);

        for (var index = 0; index < count; index++)
        {
            pairs.Add((periods[index], values[index]));
        }

        return pairs;
    }

    private static decimal? ParseValue(
        string value)
    {
        var trimmed = value.Trim();
        var isParenthesesNegative = trimmed.StartsWith('(') && trimmed.EndsWith(')');

        if (isParenthesesNegative)
        {
            trimmed = trimmed[1..^1];
        }

        var isPercent = trimmed.EndsWith('%');

        if (isPercent)
        {
            trimmed = trimmed[..^1];
        }

        trimmed = NormalizeNumericText(trimmed);

        if (decimal.TryParse(
            trimmed,
            NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var parsed))
        {
            var signed = isParenthesesNegative
                ? -parsed
                : parsed;

            return isPercent
                ? signed / 100m
                : signed;
        }

        return null;
    }

    private static bool StartsWithAlias(
        string normalizedLine,
        string normalizedAlias)
    {
        if (!normalizedLine.StartsWith(normalizedAlias, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return normalizedLine.Length == normalizedAlias.Length ||
            !char.IsLetterOrDigit(normalizedLine[normalizedAlias.Length]);
    }

    private static string NormalizeForMatch(
        string value)
    {
        var normalized = value.TrimStart().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);

            if (category != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string NormalizeNumericText(
        string value)
    {
        var trimmed = value.Trim();
        var commaIndex = trimmed.LastIndexOf(',');
        var dotIndex = trimmed.LastIndexOf('.');

        if (commaIndex >= 0 && dotIndex >= 0)
        {
            return commaIndex > dotIndex
                ? trimmed.Replace(".", string.Empty).Replace(',', '.')
                : trimmed.Replace(",", string.Empty);
        }

        if (commaIndex >= 0)
        {
            return NormalizeSingleSeparatorNumber(trimmed, commaIndex, ',');
        }

        if (dotIndex >= 0)
        {
            return NormalizeSingleSeparatorNumber(trimmed, dotIndex, '.');
        }

        return trimmed;
    }

    private static string NormalizeSingleSeparatorNumber(
        string value,
        int separatorIndex,
        char separator)
    {
        var digitsAfterSeparator = value.Length - separatorIndex - 1;
        var integerPart = value[..separatorIndex].TrimStart('-');
        var usesThousandsSeparator = digitsAfterSeparator == 3
            && integerPart != "0";

        return usesThousandsSeparator
            ? value.Replace(separator.ToString(), string.Empty)
            : value.Replace(separator, '.');
    }

    private static string NormalizePeriod(
        string year,
        string suffix)
    {
        return string.IsNullOrWhiteSpace(suffix)
            ? $"{year}A"
            : $"{year}{suffix.ToUpperInvariant()}";
    }

    private static void UpsertMetric(
        Dictionary<(string Name, string Period), StructuredFinancialMetricInput> metrics,
        StructuredFinancialMetricInput metric)
    {
        var key = (metric.Name, metric.Period);

        if (!metrics.TryGetValue(key, out var existing)
            || IsPreferred(metric, existing))
        {
            metrics[key] = metric;
        }
    }

    private static bool IsPreferred(
        StructuredFinancialMetricInput candidate,
        StructuredFinancialMetricInput existing)
    {
        var candidateConfidence = candidate.Confidence ?? 0m;
        var existingConfidence = existing.Confidence ?? 0m;

        if (candidateConfidence != existingConfidence)
        {
            return candidateConfidence > existingConfidence;
        }

        return (candidate.SourcePage ?? int.MaxValue) < (existing.SourcePage ?? int.MaxValue);
    }

    private static FinancialMetricsValidationIssue Error(
        string code,
        string message)
    {
        return new FinancialMetricsValidationIssue(
            Code: code,
            Message: message,
            MetricName: null,
            Period: null,
            Severity: "Error"
        );
    }

    private static FinancialMetricsValidationIssue Warning(
        string code,
        string message)
    {
        return new FinancialMetricsValidationIssue(
            Code: code,
            Message: message,
            MetricName: null,
            Period: null,
            Severity: "Warning"
        );
    }
}
