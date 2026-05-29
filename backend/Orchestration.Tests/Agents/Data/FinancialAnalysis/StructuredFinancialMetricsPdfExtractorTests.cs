using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsPdfExtractorTests
{
    [Theory]
    [InlineData("pdfTextExtractor")]
    [InlineData("ocrTextExtractor")]
    [InlineData("textParser")]
    [InlineData("options")]
    [InlineData("logger")]
    public void Constructor_Should_throw_for_null_dependencies(
        string dependencyName)
    {
        var action = () => _ = dependencyName switch
        {
            "pdfTextExtractor" => new StructuredFinancialMetricsPdfExtractor(
                null!,
                new FakeOcrTextExtractor([]),
                new StructuredFinancialMetricsTextParser(),
                Options.Create(new StructuredFinancialMetricsPdfExtractionOptions()),
                NullLogger<StructuredFinancialMetricsPdfExtractor>.Instance),
            "ocrTextExtractor" => new StructuredFinancialMetricsPdfExtractor(
                new FakePdfTextExtractor([]),
                null!,
                new StructuredFinancialMetricsTextParser(),
                Options.Create(new StructuredFinancialMetricsPdfExtractionOptions()),
                NullLogger<StructuredFinancialMetricsPdfExtractor>.Instance),
            "textParser" => new StructuredFinancialMetricsPdfExtractor(
                new FakePdfTextExtractor([]),
                new FakeOcrTextExtractor([]),
                null!,
                Options.Create(new StructuredFinancialMetricsPdfExtractionOptions()),
                NullLogger<StructuredFinancialMetricsPdfExtractor>.Instance),
            "options" => new StructuredFinancialMetricsPdfExtractor(
                new FakePdfTextExtractor([]),
                new FakeOcrTextExtractor([]),
                new StructuredFinancialMetricsTextParser(),
                null!,
                NullLogger<StructuredFinancialMetricsPdfExtractor>.Instance),
            "logger" => new StructuredFinancialMetricsPdfExtractor(
                new FakePdfTextExtractor([]),
                new FakeOcrTextExtractor([]),
                new StructuredFinancialMetricsTextParser(),
                Options.Create(new StructuredFinancialMetricsPdfExtractionOptions()),
                null!),
            _ => throw new ArgumentOutOfRangeException(nameof(dependencyName), dependencyName, null)
        };

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName(dependencyName);
    }

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
    public async Task ExtractAsync_Should_fallback_to_ocr_when_native_text_has_no_metrics()
    {
        var nativeExtractor = new FakePdfTextExtractor(
        [
            new StructuredFinancialMetricsExtractedPage(
                PageNumber: 1,
                Text: """
                    This annual report contains a long narrative section and notes.
                    The financial statements are embedded as scanned images below,
                    so selectable text alone does not include supported metric rows.
                    """)
        ]);
        var ocrExtractor = new FakeOcrTextExtractor(
        [
            new StructuredFinancialMetricsExtractedPage(
                PageNumber: 5,
                Text: """
                    Metric 2024A
                    Revenue 4,200
                    """,
                OcrConfidence: 0.73m)
        ]);
        var extractor = CreateExtractor(nativeExtractor, ocrExtractor);

        var result = await extractor.ExtractAsync(
            CreatePdfStream(),
            CreateRequest(),
            CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.UsedOcr.Should().BeTrue();
        ocrExtractor.CallCount.Should().Be(1);
        result.Input!.Metrics.Should().ContainSingle(metric =>
            metric.Name == "revenue" &&
            metric.Value == 4200m &&
            metric.Source == "pdf_ocr");
    }

    [Fact]
    public async Task ExtractAsync_Should_filter_metrics_below_minimum_confidence()
    {
        var nativeExtractor = new FakePdfTextExtractor(
        [
            new StructuredFinancialMetricsExtractedPage(
                PageNumber: 1,
                Text: "")
        ]);
        var ocrExtractor = new FakeOcrTextExtractor(
        [
            new StructuredFinancialMetricsExtractedPage(
                PageNumber: 2,
                Text: """
                    Metric 2024A
                    Revenue 6,000
                    """,
                OcrConfidence: 0.42m),
            new StructuredFinancialMetricsExtractedPage(
                PageNumber: 3,
                Text: """
                    Metric 2024A
                    Total Debt 1,200
                    """,
                OcrConfidence: 0.91m)
        ]);
        var extractor = CreateExtractor(
            nativeExtractor,
            ocrExtractor,
            minimumMetricConfidence: 0.8m);

        var result = await extractor.ExtractAsync(
            CreatePdfStream(),
            CreateRequest(),
            CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Input!.Metrics.Should().ContainSingle(metric =>
            metric.Name == "total_debt" &&
            metric.Confidence == 0.91m);
        result.Input.Metrics.Should().NotContain(metric => metric.Name == "revenue");
        result.Warnings.Should().Contain(issue =>
            issue.Code == "PDF_METRIC_CONFIDENCE_BELOW_THRESHOLD");
    }

    [Fact]
    public async Task ExtractAsync_Should_return_invalid_when_all_metrics_are_below_minimum_confidence()
    {
        var nativeExtractor = new FakePdfTextExtractor(
        [
            new StructuredFinancialMetricsExtractedPage(
                PageNumber: 1,
                Text: "")
        ]);
        var ocrExtractor = new FakeOcrTextExtractor(
        [
            new StructuredFinancialMetricsExtractedPage(
                PageNumber: 2,
                Text: """
                    Metric 2024A
                    Revenue 6,000
                    """,
                OcrConfidence: 0.42m)
        ]);
        var extractor = CreateExtractor(
            nativeExtractor,
            ocrExtractor,
            minimumMetricConfidence: 0.8m);

        var result = await extractor.ExtractAsync(
            CreatePdfStream(),
            CreateRequest(),
            CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Input.Should().BeNull();
        result.Errors.Should().ContainSingle(issue =>
            issue.Code == "PDF_METRICS_BELOW_CONFIDENCE_THRESHOLD");
        result.Warnings.Should().Contain(issue =>
            issue.Code == "PDF_METRIC_CONFIDENCE_BELOW_THRESHOLD");
    }

    [Fact]
    public async Task ExtractAsync_Should_fallback_to_ocr_when_native_text_is_only_whitespace_above_threshold()
    {
        var nativeExtractor = new FakePdfTextExtractor(
        [
            new StructuredFinancialMetricsExtractedPage(
                PageNumber: 1,
                Text: new string(' ', 60))
        ]);
        var ocrExtractor = new FakeOcrTextExtractor(
        [
            new StructuredFinancialMetricsExtractedPage(
                PageNumber: 3,
                Text: """
                    Metric 2024A
                    Revenue 2,500
                    """,
                OcrConfidence: 0.72m)
        ]);
        var extractor = CreateExtractor(nativeExtractor, ocrExtractor);

        var result = await extractor.ExtractAsync(
            CreatePdfStream(),
            CreateRequest(),
            CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.UsedOcr.Should().BeTrue();
        ocrExtractor.CallCount.Should().Be(1);
        result.Input!.Metrics.Should().Contain(metric =>
            metric.Name == "revenue" &&
            metric.Value == 2500m &&
            metric.Source == "pdf_ocr");
    }

    [Fact]
    public async Task ExtractAsync_Should_use_native_text_when_meaningful_text_equals_threshold()
    {
        var nativeExtractor = new FakePdfTextExtractor(
        [
            new StructuredFinancialMetricsExtractedPage(
                PageNumber: 2,
                Text: """
                    Metric 2024A
                    Revenue 1234
                    """)
        ]);
        var ocrExtractor = new FakeOcrTextExtractor([]);
        var extractor = CreateExtractor(
            nativeExtractor,
            ocrExtractor,
            nativeTextMinimumCharacters: 22);

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
                Message = "Las dependencias de OCR para PDF no están configuradas.",
                Severity = "Error"
            });
    }

    [Fact]
    public async Task ExtractAsync_Should_use_default_document_id_when_original_file_name_is_extension_only()
    {
        var nativeExtractor = new FakePdfTextExtractor(
        [
            new StructuredFinancialMetricsExtractedPage(
                PageNumber: 2,
                Text: """
                    Metric 2024A
                    Revenue 1,234
                    This text is intentionally long enough to clear the native threshold.
                    """)
        ]);
        var ocrExtractor = new FakeOcrTextExtractor([]);
        var extractor = CreateExtractor(nativeExtractor, ocrExtractor);

        var result = await extractor.ExtractAsync(
            CreatePdfStream(),
            new StructuredFinancialMetricsPdfExtractionRequest(
                DocumentId: " ",
                Company: "Vista Energy",
                Currency: "USD",
                Unit: "USD_thousand",
                OriginalFileName: ".pdf"),
            CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Input!.DocumentId.Should().Be("pdf-report");
    }

    [Fact]
    public async Task ExtractAsync_Should_honor_stream_and_options_contracts()
    {
        using var inputStream = CreatePdfStream();
        var nativeExtractor = new FakePdfTextExtractor(
            [
                new StructuredFinancialMetricsExtractedPage(
                    PageNumber: 1,
                    Text: "")
            ],
            consumeStream: true);
        var ocrExtractor = new FakeOcrTextExtractor(
        [
            new StructuredFinancialMetricsExtractedPage(
                PageNumber: 4,
                Text: """
                    Metric 2024A
                    Revenue 3,000
                    """,
                OcrConfidence: 0.81m)
        ]);
        var extractor = CreateExtractor(
            nativeExtractor,
            ocrExtractor,
            maxPages: 7);

        var result = await extractor.ExtractAsync(
            inputStream,
            CreateRequest(),
            CancellationToken.None);

        result.IsValid.Should().BeTrue();
        nativeExtractor.ReceivedStreamPosition.Should().Be(0);
        nativeExtractor.ReceivedMaxPages.Should().Be(7);
        ocrExtractor.ReceivedStreamPosition.Should().Be(0);
        ocrExtractor.ReceivedOptions.Should().NotBeNull();
        ocrExtractor.ReceivedOptions!.MaxPages.Should().Be(7);
        inputStream.CanRead.Should().BeTrue();
        inputStream.Position = 0;
        inputStream.ReadByte().Should().Be(0x25);
    }

    private static StructuredFinancialMetricsPdfExtractor CreateExtractor(
        IPdfTextExtractor nativeExtractor,
        IOcrTextExtractor ocrExtractor,
        int nativeTextMinimumCharacters = 50,
        int maxPages = 3,
        decimal minimumMetricConfidence = 0.5m)
    {
        return new StructuredFinancialMetricsPdfExtractor(
            nativeExtractor,
            ocrExtractor,
            new StructuredFinancialMetricsTextParser(),
            Options.Create(new StructuredFinancialMetricsPdfExtractionOptions
            {
                NativeTextMinimumCharacters = nativeTextMinimumCharacters,
                MaxPages = maxPages,
                MinimumMetricConfidence = minimumMetricConfidence
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
        IReadOnlyList<StructuredFinancialMetricsExtractedPage> pages,
        bool consumeStream = false)
        : IPdfTextExtractor
    {
        public long? ReceivedStreamPosition { get; private set; }

        public int? ReceivedMaxPages { get; private set; }

        public Task<IReadOnlyList<StructuredFinancialMetricsExtractedPage>> ExtractTextAsync(
            Stream pdf,
            int maxPages,
            CancellationToken cancellationToken)
        {
            ReceivedStreamPosition = pdf.Position;
            ReceivedMaxPages = maxPages;

            if (consumeStream)
            {
                pdf.CopyTo(Stream.Null);
            }

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

        public long? ReceivedStreamPosition { get; private set; }

        public StructuredFinancialMetricsPdfExtractionOptions? ReceivedOptions { get; private set; }

        public Task<IReadOnlyList<StructuredFinancialMetricsExtractedPage>> ExtractTextAsync(
            Stream pdf,
            StructuredFinancialMetricsPdfExtractionOptions options,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ReceivedStreamPosition = pdf.Position;
            ReceivedOptions = options;

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_pages ?? []);
        }
    }
}
