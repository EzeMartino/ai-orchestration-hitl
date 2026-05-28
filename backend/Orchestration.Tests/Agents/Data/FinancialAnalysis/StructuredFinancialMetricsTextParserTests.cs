using FluentAssertions;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsTextParserTests
{
    [Fact]
    public void Parse_Should_extract_known_metrics_with_periods_and_page_sources()
    {
        var result = Parse(
            source: "pdf_extraction",
            pages:
            [
                new StructuredFinancialMetricsExtractedPage(
                    PageNumber: 18,
                    Text: """
                        Metric 2024A 2025E
                        Revenue 1,647,768 1,820,000
                        Gross Profit 924,000 1,010,000
                        EBITDA 720,000 790,000
                        Net Debt 500,000 420,000
                        Free Cash Flow 120,000 150,000
                        """
                )
            ]
        );

        result.IsValid.Should().BeTrue();
        result.UsedOcr.Should().BeFalse();
        result.Input.Should().NotBeNull();
        result.Input!.Should().BeEquivalentTo(new
        {
            DocumentId = "pdf-document",
            Company = "Vista Energy",
            Currency = "USD",
            Unit = "USD_thousand"
        });
        result.Input.Metrics.Should().HaveCount(10);
        result.Input.Metrics.Should().Contain(metric =>
            metric.Name == "revenue" &&
            metric.Period == "2024A" &&
            metric.Value == 1647768m &&
            metric.Unit == "USD_thousand" &&
            metric.Currency == "USD" &&
            metric.Source == "pdf_extraction" &&
            metric.SourcePage == 18 &&
            metric.Confidence == 0.8m
        );
        result.Input.Metrics.Should().Contain(metric =>
            metric.Name == "gross_profit" &&
            metric.Period == "2025E" &&
            metric.Value == 1010000m &&
            metric.SourcePage == 18
        );
        result.Input.Metrics.Should().Contain(metric =>
            metric.Name == "ebitda" &&
            metric.Period == "2024A" &&
            metric.Value == 720000m
        );
        result.Input.Metrics.Should().Contain(metric =>
            metric.Name == "net_debt" &&
            metric.Period == "2025E" &&
            metric.Value == 420000m
        );
        result.Input.Metrics.Should().Contain(metric =>
            metric.Name == "free_cash_flow" &&
            metric.Period == "2024A" &&
            metric.Value == 120000m
        );
    }

    [Fact]
    public void Parse_Should_return_invalid_result_when_no_metrics_are_found()
    {
        var result = Parse(
            source: "pdf_extraction",
            pages:
            [
                new StructuredFinancialMetricsExtractedPage(
                    PageNumber: 4,
                    Text: "This page contains only narrative disclosure."
                )
            ]
        );

        result.IsValid.Should().BeFalse();
        result.Input.Should().BeNull();
        result.Errors.Should().ContainSingle(issue =>
            issue.Code == "PDF_METRICS_NOT_FOUND" &&
            issue.Severity == "Error"
        );
    }

    [Fact]
    public void Parse_Should_mark_ocr_source_and_lower_confidence()
    {
        var result = Parse(
            source: "pdf_ocr",
            pages:
            [
                new StructuredFinancialMetricsExtractedPage(
                    PageNumber: 22,
                    Text: """
                        Metric FY2024 2025E
                        Sales 1,647,768 1,820,000
                        FCF (20,000) 35,000
                        """,
                    OcrConfidence: 0.62m
                )
            ]
        );

        result.IsValid.Should().BeTrue();
        result.UsedOcr.Should().BeTrue();
        result.Warnings.Should().ContainSingle(issue =>
            issue.Code == "PDF_OCR_USED" &&
            issue.Severity == "Warning"
        );
        result.Input!.Metrics.Should().Contain(metric =>
            metric.Name == "revenue" &&
            metric.Period == "2024A" &&
            metric.Value == 1647768m &&
            metric.Source == "pdf_ocr" &&
            metric.SourcePage == 22 &&
            metric.Confidence == 0.62m
        );
        result.Input.Metrics.Should().Contain(metric =>
            metric.Name == "free_cash_flow" &&
            metric.Period == "2024A" &&
            metric.Value == -20000m &&
            metric.Confidence == 0.62m
        );
    }

    [Fact]
    public void Parse_Should_extract_inline_period_rows_without_header()
    {
        var result = Parse(
            source: "pdf_extraction",
            pages:
            [
                new StructuredFinancialMetricsExtractedPage(
                    PageNumber: 6,
                    Text: "Revenue 2024A 1,000 2025E 1,200"
                )
            ]
        );

        result.IsValid.Should().BeTrue();
        result.Input!.Metrics.Should().HaveCount(2);
        result.Input.Metrics.Should().Contain(metric =>
            metric.Name == "revenue" &&
            metric.Period == "2024A" &&
            metric.Value == 1000m
        );
        result.Input.Metrics.Should().Contain(metric =>
            metric.Name == "revenue" &&
            metric.Period == "2025E" &&
            metric.Value == 1200m
        );
    }

    [Fact]
    public void Parse_Should_normalize_bare_year_and_fy_year_periods()
    {
        var result = Parse(
            source: "pdf_extraction",
            pages:
            [
                new StructuredFinancialMetricsExtractedPage(
                    PageNumber: 7,
                    Text: """
                        Metric 2024 FY2024 2025E
                        Revenue 1,000 1,100 1,200
                        """
                )
            ]
        );

        result.IsValid.Should().BeTrue();
        result.Input!.Metrics.Should().Contain(metric =>
            metric.Name == "revenue" &&
            metric.Period == "2024A" &&
            metric.Value == 1000m
        );
        result.Input.Metrics.Should().Contain(metric =>
            metric.Name == "revenue" &&
            metric.Period == "2025E" &&
            metric.Value == 1200m
        );
    }

    [Fact]
    public void Parse_Should_parse_decimals_leading_minus_and_parentheses_negatives()
    {
        var result = Parse(
            source: "pdf_extraction",
            pages:
            [
                new StructuredFinancialMetricsExtractedPage(
                    PageNumber: 8,
                    Text: """
                        Metric 2024A 2025E 2026E
                        Revenue 1,000.50 -1,200.75 (300.25)
                        """
                )
            ]
        );

        result.IsValid.Should().BeTrue();
        result.Input!.Metrics.Should().Contain(metric =>
            metric.Name == "revenue" &&
            metric.Period == "2024A" &&
            metric.Value == 1000.50m
        );
        result.Input.Metrics.Should().Contain(metric =>
            metric.Name == "revenue" &&
            metric.Period == "2025E" &&
            metric.Value == -1200.75m
        );
        result.Input.Metrics.Should().Contain(metric =>
            metric.Name == "revenue" &&
            metric.Period == "2026E" &&
            metric.Value == -300.25m
        );
    }

    [Fact]
    public void Parse_Should_deduplicate_by_highest_confidence_then_earliest_page()
    {
        var result = Parse(
            source: "pdf_ocr",
            pages:
            [
                new StructuredFinancialMetricsExtractedPage(
                    PageNumber: 3,
                    Text: """
                        Metric 2024A
                        Revenue 900
                        Gross Profit 300
                        """,
                    OcrConfidence: 0.70m
                ),
                new StructuredFinancialMetricsExtractedPage(
                    PageNumber: 5,
                    Text: """
                        Metric 2024A
                        Revenue 1,000
                        Gross Profit 500
                        """,
                    OcrConfidence: 0.90m
                ),
                new StructuredFinancialMetricsExtractedPage(
                    PageNumber: 2,
                    Text: """
                        Metric 2024A
                        Gross Profit 400
                        """,
                    OcrConfidence: 0.90m
                )
            ]
        );

        result.IsValid.Should().BeTrue();
        result.Input!.Metrics.Should().ContainSingle(metric => metric.Name == "revenue")
            .Which.Should().BeEquivalentTo(new
            {
                Period = "2024A",
                Value = 1000m,
                SourcePage = 5,
                Confidence = 0.90m
            });
        result.Input.Metrics.Should().ContainSingle(metric => metric.Name == "gross_profit")
            .Which.Should().BeEquivalentTo(new
            {
                Period = "2024A",
                Value = 400m,
                SourcePage = 2,
                Confidence = 0.90m
            });
    }

    [Theory]
    [InlineData("Sales", "revenue")]
    [InlineData("Operating Income", "operating_income")]
    [InlineData("EBIT", "ebit")]
    [InlineData("Net Income", "net_income")]
    [InlineData("Current Assets", "current_assets")]
    [InlineData("Current Liabilities", "current_liabilities")]
    [InlineData("Cash", "cash")]
    [InlineData("Short Term Investments", "short_term_investments")]
    [InlineData("Receivables", "receivables")]
    [InlineData("Equity", "equity")]
    [InlineData("FCF", "free_cash_flow")]
    [InlineData("Capex", "capex")]
    [InlineData("Capital Expenditures", "capex")]
    public void Parse_Should_map_required_aliases(
        string alias,
        string expectedName)
    {
        var result = Parse(
            source: "pdf_extraction",
            pages:
            [
                new StructuredFinancialMetricsExtractedPage(
                    PageNumber: 9,
                    Text: $"""
                        Metric 2024A
                        {alias} 123
                        """
                )
            ]
        );

        result.IsValid.Should().BeTrue();
        result.Input!.Metrics.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Name = expectedName,
                Period = "2024A",
                Value = 123m
            });
    }

    private static StructuredFinancialMetricsPdfExtractionResult Parse(
        string source,
        IReadOnlyList<StructuredFinancialMetricsExtractedPage> pages)
    {
        return new StructuredFinancialMetricsTextParser().Parse(
            new StructuredFinancialMetricsTextParseRequest(
                DocumentId: "pdf-document",
                Company: "Vista Energy",
                Currency: "USD",
                Unit: "USD_thousand",
                Pages: pages,
                Source: source
            )
        );
    }
}
