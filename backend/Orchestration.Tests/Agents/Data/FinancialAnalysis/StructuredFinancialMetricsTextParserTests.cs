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
