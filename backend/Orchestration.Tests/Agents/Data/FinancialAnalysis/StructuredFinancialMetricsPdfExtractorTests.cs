using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsPdfExtractorTests
{
    [Fact]
    public async Task ExtractAsync_Should_use_native_text_when_text_is_sufficient()
    {
        var nativeExtractor = new FakePdfTextExtractor(
        [
            new StructuredFinancialMetricsExtractedPage(
                PageNumber: 2,
                Text: """
                    Metric 2024A
                    Revenue 1,234
                    This text is intentionally long enough to clear the native threshold.
                    """
            )
        ]);
        var ocrExtractor = new FakeOcrTextExtractor([]);
        var extractor = CreateExtractor(nativeExtractor, ocrExtractor);

        var result = await extractor.ExtractAsync(
            CreatePdfStream(),
            CreateRequest(),
            CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.UsedOcr.Should().BeFalse();
        ocrExtractor.CallCount.Should().Be(0);
        result.Input!.Metrics.Should().Contain(metric =>
            metric.Name == "revenue" &&
            metric.Value == 1234m &&
            metric.Source == "pdf_extraction");
    }

    [Fact]
    public async Task ExtractAsync_Should_fallback_to_ocr_when_native_text_is_too_short()
    {
        var nativeExtractor = new FakePdfTextExtractor(
        [
            new StructuredFinancialMetricsExtractedPage(
                PageNumber: 1,
                Text: "   ")
        ]);
        var ocrExtractor = new FakeOcrTextExtractor(
        [
            new StructuredFinancialMetricsExtractedPage(
                PageNumber: 4,
                Text: """
                    Metric 2024A
                    Total Debt 900
                    Interest Expense 45
                    """,
                OcrConfidence: 0.64m)
        ]);
        var extractor = CreateExtractor(nativeExtractor, ocrExtractor);

        var result = await extractor.ExtractAsync(
            CreatePdfStream(),
            CreateRequest(),
            CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.UsedOcr.Should().BeTrue();
        ocrExtractor.CallCount.Should().Be(1);
        result.Warnings.Should().ContainSingle(issue =>
            issue.Code == "PDF_OCR_USED" &&
            issue.Severity == "Warning");
        result.Input!.Metrics.Should().Contain(metric =>
            metric.Name == "total_debt" &&
            metric.Source == "pdf_ocr" &&
            metric.Confidence == 0.64m);
        result.Input.Metrics.Should().Contain(metric =>
            metric.Name == "interest_expense" &&
            metric.Source == "pdf_ocr" &&
            metric.Confidence == 0.64m);
    }

    [Fact]
    public async Task ExtractAsync_Should_return_dependency_error_when_ocr_is_not_configured()
    {
        var nativeExtractor = new FakePdfTextExtractor(
        [
            new StructuredFinancialMetricsExtractedPage(
                PageNumber: 1,
                Text: "")
        ]);
        var ocrExtractor = new FakeOcrTextExtractor(
            new PdfOcrDependencyException("PDF OCR dependencies are not configured."));
        var extractor = CreateExtractor(nativeExtractor, ocrExtractor);

        var result = await extractor.ExtractAsync(
            CreatePdfStream(),
            CreateRequest(),
            CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.UsedOcr.Should().BeTrue();
        result.Input.Should().BeNull();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Code = "PDF_OCR_NOT_CONFIGURED",
                Message = "PDF OCR dependencies are not configured.",
                Severity = "Error"
            });
    }

    private static StructuredFinancialMetricsPdfExtractor CreateExtractor(
        IPdfTextExtractor nativeExtractor,
        IOcrTextExtractor ocrExtractor)
    {
        return new StructuredFinancialMetricsPdfExtractor(
            nativeExtractor,
            ocrExtractor,
            new StructuredFinancialMetricsTextParser(),
            Options.Create(new StructuredFinancialMetricsPdfExtractionOptions
            {
                NativeTextMinimumCharacters = 50,
                MaxPages = 3
            }),
            NullLogger<StructuredFinancialMetricsPdfExtractor>.Instance);
    }

    private static StructuredFinancialMetricsPdfExtractionRequest CreateRequest()
    {
        return new StructuredFinancialMetricsPdfExtractionRequest(
            DocumentId: "  pdf-doc-1  ",
            Company: "Vista Energy",
            Currency: "USD",
            Unit: "USD_thousand",
            OriginalFileName: "vista-report.pdf");
    }

    private static MemoryStream CreatePdfStream()
    {
        return new MemoryStream([0x25, 0x50, 0x44, 0x46]);
    }

    private sealed class FakePdfTextExtractor(
        IReadOnlyList<StructuredFinancialMetricsExtractedPage> pages)
        : IPdfTextExtractor
    {
        public Task<IReadOnlyList<StructuredFinancialMetricsExtractedPage>> ExtractTextAsync(
            Stream pdf,
            int maxPages,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(pages);
        }
    }

    private sealed class FakeOcrTextExtractor : IOcrTextExtractor
    {
        private readonly IReadOnlyList<StructuredFinancialMetricsExtractedPage>? _pages;
        private readonly Exception? _exception;

        public FakeOcrTextExtractor(
            IReadOnlyList<StructuredFinancialMetricsExtractedPage> pages)
        {
            _pages = pages;
        }

        public FakeOcrTextExtractor(
            Exception exception)
        {
            _exception = exception;
        }

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<StructuredFinancialMetricsExtractedPage>> ExtractTextAsync(
            Stream pdf,
            StructuredFinancialMetricsPdfExtractionOptions options,
            CancellationToken cancellationToken)
        {
            CallCount++;

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_pages ?? []);
        }
    }
}
