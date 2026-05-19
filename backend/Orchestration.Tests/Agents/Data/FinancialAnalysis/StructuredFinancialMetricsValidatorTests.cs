using System.Text.Json;
using FluentAssertions;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsValidatorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

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
        var valid = LoadFixture("structured_financial_metrics_input_valid.json");
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
            Metrics: metrics ?? [CreateMetric()]
        );
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
