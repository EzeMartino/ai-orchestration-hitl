using FluentAssertions;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsCsvParserTests
{
    [Fact]
    public void Parse_Report_summary_Should_copy_it_to_structured_input()
    {
        var summary = new FinancialReportSummaryInput(
            "CSV report",
            250m,
            3,
            new DateTimeOffset(2026, 7, 12, 11, 0, 0, TimeSpan.Zero));

        var result = Parse("""
            name,period,value
            Revenue,2024A,100
            """, summary);

        result.IsValid.Should().BeTrue();
        result.Input!.ReportSummary.Should().BeSameAs(summary);
    }

    [Fact]
    public void Parse_Should_convert_simple_csv_to_structured_input()
    {
        var result = Parse("""
            name,period,value,unit,currency,source,sourcePage,confidence
            Revenue,2024A,1647768,USD_thousand,USD,manual_upload,18,0.9
            Gross Profit,2024A,924000,USD_thousand,USD,manual_upload,18,0.85
            """);

        result.IsValid.Should().BeTrue();
        result.Input.Should().NotBeNull();
        result.Input!.DocumentId.Should().Be("manual-csv-input");
        result.Input.Company.Should().Be("Manual Test Co");
        result.Input.Metrics.Should().HaveCount(2);
        result.Input.Metrics[0].Should().BeEquivalentTo(new
        {
            Name = "Revenue",
            Period = "2024A",
            Value = 1647768m,
            Unit = "USD_thousand",
            Currency = "USD",
            Source = "manual_upload",
            SourcePage = 18,
            Confidence = 0.9m
        });
    }

    [Fact]
    public void Parse_Should_support_quoted_fields()
    {
        var result = Parse("""
            name,period,value,source
            "Gross Profit",2024A,924000,"manual, upload"
            """);

        result.IsValid.Should().BeTrue();
        result.Input!.Metrics.Should().ContainSingle()
            .Which.Source.Should().Be("manual, upload");
    }

    [Fact]
    public void Parse_Should_reject_missing_required_header()
    {
        var result = Parse("""
            name,value
            Revenue,1647768
            """);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(issue =>
            issue.Code == "CSV_REQUIRED_HEADER_MISSING" &&
            issue.Message.Contains("period", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Parse_Should_reject_invalid_decimal()
    {
        var result = Parse("""
            name,period,value
            Revenue,2024A,not-a-number
            """);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(issue => issue.Code == "CSV_INVALID_DECIMAL");
    }

    [Fact]
    public void Parse_Should_reject_invalid_source_page()
    {
        var result = Parse("""
            name,period,value,sourcePage
            Revenue,2024A,1647768,page-18
            """);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(issue => issue.Code == "CSV_INVALID_INT");
    }

    [Fact]
    public void Parse_Should_reject_unclosed_quote()
    {
        var result = Parse("""
            name,period,value
            "Revenue,2024A,1647768
            """);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(issue => issue.Code == "CSV_UNCLOSED_QUOTE");
    }

    private static StructuredFinancialMetricsCsvParseResult Parse(
        string csv,
        FinancialReportSummaryInput? reportSummary = null)
    {
        return new StructuredFinancialMetricsCsvParser().Parse(
            new StructuredFinancialMetricsCsvInput(
                DocumentId: "manual-csv-input",
                Company: "Manual Test Co",
                Currency: "USD",
                Unit: "USD_thousand",
                Csv: csv,
                ReportSummary: reportSummary
            )
        );
    }
}
