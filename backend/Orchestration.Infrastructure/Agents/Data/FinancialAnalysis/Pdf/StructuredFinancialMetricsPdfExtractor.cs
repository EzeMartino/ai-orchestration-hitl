using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;

public sealed class StructuredFinancialMetricsPdfExtractor
    : IStructuredFinancialMetricsPdfExtractor
{
    private const string NativePdfSource = "pdf_extraction";
    private const string OcrPdfSource = "pdf_ocr";
    private const string OcrNotConfiguredCode = "PDF_OCR_NOT_CONFIGURED";
    private const string OcrNotConfiguredMessage = "PDF OCR dependencies are not configured.";

    private readonly IPdfTextExtractor _pdfTextExtractor;
    private readonly IOcrTextExtractor _ocrTextExtractor;
    private readonly IStructuredFinancialMetricsTextParser _textParser;
    private readonly StructuredFinancialMetricsPdfExtractionOptions _options;
    private readonly ILogger<StructuredFinancialMetricsPdfExtractor> _logger;

    public StructuredFinancialMetricsPdfExtractor(
        IPdfTextExtractor pdfTextExtractor,
        IOcrTextExtractor ocrTextExtractor,
        IStructuredFinancialMetricsTextParser textParser,
        IOptions<StructuredFinancialMetricsPdfExtractionOptions> options,
        ILogger<StructuredFinancialMetricsPdfExtractor> logger)
    {
        _pdfTextExtractor = pdfTextExtractor;
        _ocrTextExtractor = ocrTextExtractor;
        _textParser = textParser;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StructuredFinancialMetricsPdfExtractionResult> ExtractAsync(
        Stream pdf,
        StructuredFinancialMetricsPdfExtractionRequest request,
        CancellationToken cancellationToken)
    {
        using var seekablePdf = new MemoryStream();
        await pdf.CopyToAsync(seekablePdf, cancellationToken);

        seekablePdf.Position = 0;
        var nativePages = await _pdfTextExtractor.ExtractTextAsync(
            seekablePdf,
            _options.MaxPages,
            cancellationToken);

        if (GetTextLength(nativePages) >= _options.NativeTextMinimumCharacters)
        {
            return Parse(request, nativePages, NativePdfSource);
        }

        seekablePdf.Position = 0;

        try
        {
            var ocrPages = await _ocrTextExtractor.ExtractTextAsync(
                seekablePdf,
                _options,
                cancellationToken);

            return Parse(request, ocrPages, OcrPdfSource);
        }
        catch (PdfOcrDependencyException exception)
        {
            _logger.LogWarning(exception, "PDF OCR dependencies are not configured.");

            return new StructuredFinancialMetricsPdfExtractionResult(
                IsValid: false,
                Input: null,
                Errors:
                [
                    new FinancialMetricsValidationIssue(
                        Code: OcrNotConfiguredCode,
                        Message: OcrNotConfiguredMessage,
                        MetricName: null,
                        Period: null,
                        Severity: "Error")
                ],
                Warnings: [],
                UsedOcr: true);
        }
    }

    private StructuredFinancialMetricsPdfExtractionResult Parse(
        StructuredFinancialMetricsPdfExtractionRequest request,
        IReadOnlyList<StructuredFinancialMetricsExtractedPage> pages,
        string source)
    {
        return _textParser.Parse(
            new StructuredFinancialMetricsTextParseRequest(
                DocumentId: GetDocumentId(request),
                Company: request.Company,
                Currency: request.Currency,
                Unit: request.Unit,
                Pages: pages,
                Source: source));
    }

    private static int GetTextLength(
        IReadOnlyList<StructuredFinancialMetricsExtractedPage> pages)
    {
        return pages.Sum(page => page.Text.Length);
    }

    private static string GetDocumentId(
        StructuredFinancialMetricsPdfExtractionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.DocumentId))
        {
            return request.DocumentId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.OriginalFileName))
        {
            return Path.GetFileNameWithoutExtension(request.OriginalFileName.Trim());
        }

        return "pdf-report";
    }
}
