using System.Text.Json;
using FluentAssertions;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsValidatorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void FinancialReportSummaryValidator_Valid_input_Should_normalize_report_name_and_accept_zero_values()
    {
        var submittedAt = new DateTimeOffset(2026, 7, 12, 10, 30, 0, TimeSpan.Zero);
        var input = new FinancialReportSummaryInput("  July report  ", 0m, 0, submittedAt);

        var result = FinancialReportSummaryValidator.Validate(input);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Summary.Should().Be(new FinancialReportSummary(
            "July report",
            0m,
            0,
            submittedAt));
    }

    [Fact]
    public void FinancialReportSummaryValidator_Null_input_Should_return_required_issue()
    {
        var result = FinancialReportSummaryValidator.Validate(null);

        result.IsValid.Should().BeFalse();
        result.Summary.Should().BeNull();
        result.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("REPORT_SUMMARY_REQUIRED");
    }

    [Fact]
    public void FinancialReportSummaryValidator_Missing_fields_Should_return_required_issues()
    {
        var input = new FinancialReportSummaryInput(" ", null, null, null);

        var result = FinancialReportSummaryValidator.Validate(input);

        result.IsValid.Should().BeFalse();
        result.Summary.Should().BeNull();
        result.Errors.Select(issue => issue.Code).Should().BeEquivalentTo(
        [
            "REPORT_NAME_REQUIRED",
            "TOTAL_AMOUNT_REQUIRED",
            "TRANSACTION_COUNT_REQUIRED",
            "SUBMITTED_AT_REQUIRED"
        ]);
    }

    [Fact]
    public void FinancialReportSummaryValidator_Invalid_values_Should_return_invalid_issues()
    {
        var input = new FinancialReportSummaryInput(
            "Report",
            -0.01m,
            -1,
            default(DateTimeOffset));

        var result = FinancialReportSummaryValidator.Validate(input);

        result.IsValid.Should().BeFalse();
        result.Summary.Should().BeNull();
        result.Errors.Select(issue => issue.Code).Should().BeEquivalentTo(
        [
            "TOTAL_AMOUNT_INVALID",
            "TRANSACTION_COUNT_INVALID",
            "SUBMITTED_AT_INVALID"
        ]);
    }

    [Fact]
    public void Structured_input_json_Malformed_summary_timestamp_Should_return_stable_invalid_issue()
    {
        var input = JsonSerializer.Deserialize<StructuredFinancialMetricsInput>(
            """
            {
              "documentId": "json-input",
              "metrics": [],
              "reportSummary": {
                "reportName": "report.json",
                "totalAmount": 10,
                "transactionCount": 1,
                "submittedAt": "not-an-iso-timestamp"
              }
            }
            """,
            JsonOptions);

        var result = FinancialReportSummaryValidator.Validate(input!.ReportSummary);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("SUBMITTED_AT_INVALID");
    }

    [Fact]
    public void Csv_input_json_Malformed_summary_timestamp_Should_return_stable_invalid_issue()
    {
        var input = JsonSerializer.Deserialize<StructuredFinancialMetricsCsvInput>(
            """
            {
              "documentId": "csv-input",
              "csv": "name,period,value\nRevenue,2024A,10",
              "reportSummary": {
                "reportName": "report.csv",
                "totalAmount": 10,
                "transactionCount": 1,
                "submittedAt": "not-an-iso-timestamp"
              }
            }
            """,
            JsonOptions);

        var result = FinancialReportSummaryValidator.Validate(input!.ReportSummary);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("SUBMITTED_AT_INVALID");
    }

    [Fact]
    public void Summary_json_Valid_timestamp_Should_preserve_exact_offset()
    {
        var input = JsonSerializer.Deserialize<FinancialReportSummaryInput>(
            """
            {
              "reportName": "report.json",
              "totalAmount": 10,
              "transactionCount": 1,
              "submittedAt": "2026-07-12T18:30:00-03:00"
            }
            """,
            JsonOptions);

        input!.SubmittedAt.Should().Be(new DateTimeOffset(
            2026, 7, 12, 18, 30, 0, TimeSpan.FromHours(-3)));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"submittedAt\":null}")]
    public void Summary_json_Missing_or_null_timestamp_Should_return_required_issue(
        string json)
    {
        var input = JsonSerializer.Deserialize<FinancialReportSummaryInput>(
            json,
            JsonOptions);

        input!.SubmittedAt.Should().BeNull();
        FinancialReportSummaryValidator.Validate(input).Errors
            .Should().Contain(issue => issue.Code == "SUBMITTED_AT_REQUIRED");
    }

    [Fact]
    public void Validate_Invalid_report_summary_Should_map_summary_issues()
    {
        var input = CreateInput() with
        {
            ReportSummary = new FinancialReportSummaryInput("Report", -1m, 1, DateTimeOffset.UtcNow)
        };

        var result = Validate(input);

        result.IsValid.Should().BeFalse();
        result.ReportSummary.Should().BeNull();
        result.Errors.Should().Contain(issue =>
            issue.Code == "TOTAL_AMOUNT_INVALID" &&
            issue.Severity == "Error");
    }

    [Fact]
    public void Validate_Valid_report_summary_Should_include_normalized_summary()
    {
        var result = Validate(CreateInput());

        result.IsValid.Should().BeTrue();
        result.ReportSummary.Should().Be(new FinancialReportSummary(
            "Test report",
            1000m,
            2,
            new DateTimeOffset(2026, 7, 12, 10, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Validate_Should_reject_empty_document_id()
    {
        var result = Validate(CreateInput(documentId: ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(issue => issue.Code == "DOCUMENT_ID_REQUIRED");
    }

    [Fact]
    public void Validate_Should_reject_empty_metrics()
    {
        var result = Validate(CreateInput(metrics: []));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(issue => issue.Code == "METRICS_REQUIRED");
    }

    [Fact]
    public void Validate_Should_reject_metric_with_empty_name()
    {
        var result = Validate(CreateInput(metrics: [CreateMetric(name: " ")]));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(issue => issue.Code == "METRIC_NAME_REQUIRED");
    }

    [Fact]
    public void Validate_Should_reject_metric_with_empty_period()
    {
        var result = Validate(CreateInput(metrics: [CreateMetric(period: " ")]));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(issue => issue.Code == "METRIC_PERIOD_REQUIRED");
    }

    [Fact]
    public void Validate_Should_reject_metric_with_null_value()
    {
        var result = Validate(CreateInput(metrics: [CreateMetric(value: null)]));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(issue => issue.Code == "METRIC_VALUE_REQUIRED");
    }

    [Fact]
    public void Validate_Should_normalize_metric_names()
    {
        var result = Validate(CreateInput(metrics:
        [
            CreateMetric(name: " Gross Profit "),
            CreateMetric(name: "EBITDA Margin", period: "2025E")
        ]));

        result.Metrics.Select(metric => metric.Name)
            .Should()
            .BeEquivalentTo(["gross_profit", "ebitda_margin"]);
    }

    [Fact]
    public void Validate_Should_normalize_period()
    {
        var result = Validate(CreateInput(metrics: [CreateMetric(period: " 2025e ")]));

        result.Metrics.Should().ContainSingle()
            .Which.Period.Should().Be("2025E");
    }

    [Fact]
    public void Validate_Should_apply_global_unit_and_currency_defaults()
    {
        var result = Validate(CreateInput(metrics:
        [
            CreateMetric(unit: null, currency: null)
        ]));

        result.IsValid.Should().BeTrue();
        result.Metrics.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Unit = "USD_thousand",
                Currency = "USD"
            });
        result.Warnings.Should().Contain(issue => issue.Code == "METRIC_UNIT_DEFAULTED");
        result.Warnings.Should().Contain(issue => issue.Code == "METRIC_CURRENCY_DEFAULTED");
    }

    [Fact]
    public void Validate_Should_apply_default_source()
    {
        var result = Validate(CreateInput(metrics: [CreateMetric(source: null)]));

        result.Metrics.Should().ContainSingle()
            .Which.Source.Should().Be("structured_input");
        result.Warnings.Should().Contain(issue => issue.Code == "METRIC_SOURCE_DEFAULTED");
    }

    [Fact]
    public void Validate_Should_apply_default_confidence()
    {
        var result = Validate(CreateInput(metrics: [CreateMetric(confidence: null)]));

        result.Metrics.Should().ContainSingle()
            .Which.Confidence.Should().Be(0.75m);
        result.Warnings.Should().Contain(issue => issue.Code == "METRIC_CONFIDENCE_DEFAULTED");
    }

    [Fact]
    public void Validate_Should_clamp_invalid_confidence_and_warn()
    {
        var result = Validate(CreateInput(metrics:
        [
            CreateMetric(name: "revenue", confidence: 1.4m),
            CreateMetric(name: "gross_profit", confidence: -0.1m)
        ]));

        result.Metrics.Should().Contain(metric =>
            metric.Name == "revenue" &&
            metric.Confidence == 1m
        );
        result.Metrics.Should().Contain(metric =>
            metric.Name == "gross_profit" &&
            metric.Confidence == 0m
        );
        result.Warnings.Should().Contain(issue => issue.Code == "METRIC_CONFIDENCE_CLAMPED");
    }

    [Fact]
    public void Validate_Should_handle_invalid_source_page()
    {
        var result = Validate(CreateInput(metrics: [CreateMetric(sourcePage: 0)]));

        result.Metrics.Should().ContainSingle()
            .Which.SourcePage.Should().BeNull();
        result.Warnings.Should().Contain(issue => issue.Code == "METRIC_SOURCE_PAGE_IGNORED");
    }

    [Fact]
    public void Validate_Should_warn_unknown_metric_name()
    {
        var result = Validate(CreateInput(metrics: [CreateMetric(name: "Adjusted Operating Magic")]));

        result.IsValid.Should().BeTrue();
        result.Metrics.Should().ContainSingle()
            .Which.Name.Should().Be("adjusted_operating_magic");
        result.Warnings.Should().Contain(issue => issue.Code == "UNKNOWN_METRIC_NAME");
    }

    [Fact]
    public void Validate_Should_handle_duplicate_metrics_by_confidence()
    {
        var result = Validate(CreateInput(metrics:
        [
            CreateMetric(name: "revenue", value: 100m, confidence: 0.6m),
            CreateMetric(name: "Revenue", value: 120m, confidence: 0.9m)
        ]));

        result.IsValid.Should().BeTrue();
        result.Metrics.Should().ContainSingle()
            .Which.Value.Should().Be(120m);
        result.Warnings.Should().Contain(issue => issue.Code == "DUPLICATE_METRIC_REPLACED");
    }

    [Fact]
    public void Validate_Should_preserve_first_duplicate_when_confidence_is_equal()
    {
        var result = Validate(CreateInput(metrics:
        [
            CreateMetric(name: "revenue", value: 100m, confidence: 0.8m),
            CreateMetric(name: "Revenue", value: 120m, confidence: 0.8m)
        ]));

        result.IsValid.Should().BeTrue();
        result.Metrics.Should().ContainSingle()
            .Which.Value.Should().Be(100m);
        result.Warnings.Should().Contain(issue => issue.Code == "DUPLICATE_METRIC_IGNORED");
    }

    [Fact]
    public void FinancialMetricInputMapper_Should_map_valid_input_to_financial_metrics()
    {
        var validationResult = Validate(CreateInput(metrics:
        [
            CreateMetric(name: "Gross Profit", period: " 2025e ", value: 924000m)
        ]));
        var mapper = new FinancialMetricInputMapper();

        var metrics = mapper.MapToFinancialMetrics(validationResult);

        metrics.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new FinancialMetric(
                Name: "gross_profit",
                Period: "2025E",
                Value: 924000m,
                Unit: "USD_thousand",
                Statement: "structured_input",
                Source: "manual_upload",
                Currency: "USD",
                SourcePage: 18,
                Confidence: 0.9m
            ));
    }

    [Fact]
    public void FinancialMetricInputMapper_Should_not_map_invalid_input()
    {
        var validationResult = Validate(CreateInput(documentId: ""));
        var mapper = new FinancialMetricInputMapper();

        mapper.MapToFinancialMetrics(validationResult)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Fixtures_Should_deserialize_and_validate_expected_results()
    {
        var valid = LoadFixture("structured_financial_metrics_input_valid.json") with
        {
            ReportSummary = CreateReportSummary()
        };
        var invalid = LoadFixture("structured_financial_metrics_input_invalid.json");

        var validResult = Validate(valid);
        var invalidResult = Validate(invalid);

        validResult.IsValid.Should().BeTrue();
        validResult.Metrics.Should().Contain(metric =>
            metric.Name == "revenue" &&
            metric.Period == "2024A" &&
            metric.Unit == "USD_thousand" &&
            metric.SourcePage == 18
        );
        validResult.Metrics.Should().Contain(metric =>
            metric.Name == "gross_profit" &&
            metric.Source == "structured_input" &&
            metric.Confidence == 0.85m
        );
        invalidResult.IsValid.Should().BeFalse();
        invalidResult.Errors.Should().Contain(issue => issue.Code == "DOCUMENT_ID_REQUIRED");
        invalidResult.Errors.Should().Contain(issue => issue.Code == "METRIC_NAME_REQUIRED");
        invalidResult.Errors.Should().Contain(issue => issue.Code == "METRIC_VALUE_REQUIRED");
    }

    private static FinancialMetricsValidationResult Validate(
        StructuredFinancialMetricsInput input)
    {
        return new StructuredFinancialMetricsValidator().Validate(input);
    }

    private static StructuredFinancialMetricsInput CreateInput(
        string documentId = "test-document",
        IReadOnlyList<StructuredFinancialMetricInput>? metrics = null)
    {
        return new StructuredFinancialMetricsInput(
            DocumentId: documentId,
            Company: "Vista Energy",
            Currency: "USD",
            Unit: "USD_thousand",
            Metrics: metrics ?? [CreateMetric()],
            ReportSummary: CreateReportSummary()
        );
    }

    private static FinancialReportSummaryInput CreateReportSummary()
    {
        return new FinancialReportSummaryInput(
            ReportName: "  Test report  ",
            TotalAmount: 1000m,
            TransactionCount: 2,
            SubmittedAt: new DateTimeOffset(2026, 7, 12, 10, 30, 0, TimeSpan.Zero));
    }

    private static StructuredFinancialMetricInput CreateMetric(
        string name = "revenue",
        string period = "2024A",
        decimal? value = 100m,
        string? unit = "USD_thousand",
        string? currency = "USD",
        string? source = "manual_upload",
        int? sourcePage = 18,
        decimal? confidence = 0.9m)
    {
        return new StructuredFinancialMetricInput(
            Name: name,
            Period: period,
            Value: value,
            Unit: unit,
            Currency: currency,
            Source: source,
            SourcePage: sourcePage,
            Confidence: confidence
        );
    }

    private static StructuredFinancialMetricsInput LoadFixture(
        string fileName)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "FinancialInput",
            fileName
        );
        var json = File.ReadAllText(path);

        return JsonSerializer.Deserialize<StructuredFinancialMetricsInput>(
            json,
            JsonOptions
        ) ?? throw new InvalidOperationException($"{fileName} could not be deserialized.");
    }
}
