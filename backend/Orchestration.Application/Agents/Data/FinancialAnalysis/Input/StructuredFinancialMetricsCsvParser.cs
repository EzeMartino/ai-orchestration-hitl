using System.Globalization;
using System.Text;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsCsvParser
    : IStructuredFinancialMetricsCsvParser
{
    private static readonly string[] RequiredHeaders =
    [
        "name",
        "period",
        "value"
    ];

    public StructuredFinancialMetricsCsvParseResult Parse(
        StructuredFinancialMetricsCsvInput input)
    {
        var errors = new List<FinancialMetricsValidationIssue>();
        var warnings = new List<FinancialMetricsValidationIssue>();

        if (input is null)
        {
            errors.Add(Error(
                "CSV_INPUT_REQUIRED",
                "Structured financial metrics CSV input is required."
            ));

            return Failed(errors, warnings);
        }

        if (string.IsNullOrWhiteSpace(input.Csv))
        {
            errors.Add(Error(
                "CSV_REQUIRED",
                "CSV content is required."
            ));

            return Failed(errors, warnings);
        }

        var records = ParseRecords(input.Csv, errors);

        if (errors.Count > 0)
        {
            return Failed(errors, warnings);
        }

        if (records.Count == 0)
        {
            errors.Add(Error(
                "CSV_HEADER_REQUIRED",
                "CSV header row is required."
            ));

            return Failed(errors, warnings);
        }

        var headerMap = BuildHeaderMap(records[0]);

        foreach (var requiredHeader in RequiredHeaders)
        {
            if (!headerMap.ContainsKey(requiredHeader))
            {
                errors.Add(Error(
                    "CSV_REQUIRED_HEADER_MISSING",
                    $"CSV header '{requiredHeader}' is required."
                ));
            }
        }

        if (errors.Count > 0)
        {
            return Failed(errors, warnings);
        }

        var metrics = new List<StructuredFinancialMetricInput>();

        for (var index = 1; index < records.Count; index++)
        {
            var row = records[index];
            var rowNumber = index + 1;

            if (row.Length > headerMap.Count)
            {
                errors.Add(Error(
                    "CSV_ROW_INVALID_COLUMN_COUNT",
                    $"CSV row {rowNumber} has more values than the header row."
                ));

                continue;
            }

            metrics.Add(new StructuredFinancialMetricInput(
                Name: Get(row, headerMap, "name"),
                Period: Get(row, headerMap, "period"),
                Value: ParseDecimal(Get(row, headerMap, "value"), rowNumber, "value", errors),
                Unit: NullIfWhiteSpace(Get(row, headerMap, "unit")),
                Currency: NullIfWhiteSpace(Get(row, headerMap, "currency")),
                Source: NullIfWhiteSpace(Get(row, headerMap, "source")),
                SourcePage: ParseInt(Get(row, headerMap, "sourcepage"), rowNumber, "sourcePage", errors),
                Confidence: ParseDecimal(Get(row, headerMap, "confidence"), rowNumber, "confidence", errors)
            ));
        }

        if (errors.Count > 0)
        {
            return Failed(errors, warnings);
        }

        return new StructuredFinancialMetricsCsvParseResult(
            IsValid: true,
            Input: new StructuredFinancialMetricsInput(
                DocumentId: input.DocumentId,
                Company: input.Company,
                Currency: input.Currency,
                Unit: input.Unit,
                Metrics: metrics
            ),
            Errors: [],
            Warnings: warnings
        );
    }

    private static Dictionary<string, int> BuildHeaderMap(
        IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = 0; index < headers.Count; index++)
        {
            var normalized = NormalizeHeader(headers[index]);

            if (!string.IsNullOrWhiteSpace(normalized) && !map.ContainsKey(normalized))
            {
                map[normalized] = index;
            }
        }

        return map;
    }

    private static IReadOnlyList<string[]> ParseRecords(
        string csv,
        List<FinancialMetricsValidationIssue> errors)
    {
        var records = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];

            if (inQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < csv.Length && csv[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            if (character == '"')
            {
                inQuotes = true;
                continue;
            }

            if (character == ',')
            {
                row.Add(field.ToString());
                field.Clear();
                continue;
            }

            if (character == '\r' || character == '\n')
            {
                if (character == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n')
                {
                    index++;
                }

                AddRecord(records, row, field);
                continue;
            }

            field.Append(character);
        }

        if (inQuotes)
        {
            errors.Add(Error(
                "CSV_UNCLOSED_QUOTE",
                "CSV contains an unclosed quoted field."
            ));

            return [];
        }

        AddRecord(records, row, field);

        return records;
    }

    private static void AddRecord(
        List<string[]> records,
        List<string> row,
        StringBuilder field)
    {
        row.Add(field.ToString());
        field.Clear();

        if (row.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            records.Add(row.ToArray());
        }

        row.Clear();
    }

    private static decimal? ParseDecimal(
        string value,
        int rowNumber,
        string columnName,
        List<FinancialMetricsValidationIssue> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (decimal.TryParse(
            value.Trim(),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var parsed))
        {
            return parsed;
        }

        errors.Add(Error(
            "CSV_INVALID_DECIMAL",
            $"CSV row {rowNumber} has an invalid decimal value for '{columnName}'."
        ));

        return null;
    }

    private static int? ParseInt(
        string value,
        int rowNumber,
        string columnName,
        List<FinancialMetricsValidationIssue> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (int.TryParse(
            value.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed))
        {
            return parsed;
        }

        errors.Add(Error(
            "CSV_INVALID_INT",
            $"CSV row {rowNumber} has an invalid integer value for '{columnName}'."
        ));

        return null;
    }

    private static string Get(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> headerMap,
        string header)
    {
        return headerMap.TryGetValue(header, out var index) && index < row.Count
            ? row[index].Trim()
            : "";
    }

    private static string NormalizeHeader(
        string value)
    {
        return value.Trim()
            .Replace("_", "", StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static string? NullIfWhiteSpace(
        string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static StructuredFinancialMetricsCsvParseResult Failed(
        IReadOnlyList<FinancialMetricsValidationIssue> errors,
        IReadOnlyList<FinancialMetricsValidationIssue> warnings)
    {
        return new StructuredFinancialMetricsCsvParseResult(
            IsValid: false,
            Input: null,
            Errors: errors,
            Warnings: warnings
        );
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
}
