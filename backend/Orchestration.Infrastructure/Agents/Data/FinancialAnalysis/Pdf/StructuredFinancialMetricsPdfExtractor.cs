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
    private const string OcrNotConfiguredMessage = "Las dependencias de OCR para PDF no están configuradas.";

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
        ArgumentNullException.ThrowIfNull(pdfTextExtractor);
        ArgumentNullException.ThrowIfNull(ocrTextExtractor);
        ArgumentNullException.ThrowIfNull(textParser);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

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
            var nativeResult = Parse(request, nativePages, NativePdfSource);

            if (nativeResult.IsValid)
            {
                return nativeResult;
            }
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
        var result = _textParser.Parse(
            new StructuredFinancialMetricsTextParseRequest(
                DocumentId: GetDocumentId(request),
                Company: request.Company,
                Currency: request.Currency,
                Unit: request.Unit,
                Pages: pages,
                Source: source));

        return ApplyMinimumMetricConfidence(result);
    }

    private StructuredFinancialMetricsPdfExtractionResult ApplyMinimumMetricConfidence(
        StructuredFinancialMetricsPdfExtractionResult result)
    {
        if (!result.IsValid || result.Input is null)
        {
            return result;
        }

        var acceptedMetrics = result.Input.Metrics
            .Where(metric => (metric.Confidence ?? 0m) >= _options.MinimumMetricConfidence)
            .ToArray();

        if (acceptedMetrics.Length == result.Input.Metrics.Count)
        {
            return result;
        }

        var warnings = result.Warnings
            .Append(new FinancialMetricsValidationIssue(
                Code: "PDF_METRIC_CONFIDENCE_BELOW_THRESHOLD",
                Message: "Se ignoraron una o más métricas del PDF debido a que la confianza estaba por debajo del umbral configurado.",
                MetricName: null,
                Period: null,
                Severity: "Warning"))
            .ToArray();

        if (acceptedMetrics.Length == 0)
        {
            return result with
            {
                IsValid = false,
                Input = null,
                Errors =
                [
                    new FinancialMetricsValidationIssue(
                        Code: "PDF_METRICS_BELOW_CONFIDENCE_THRESHOLD",
                        Message: "Ninguna métrica financiera soportada cumplió con el umbral de confianza configurado.",
                        MetricName: null,
                        Period: null,
                        Severity: "Error")
                ],
                Warnings = warnings
            };
        }

        return result with
        {
            Input = result.Input with
            {
                Metrics = acceptedMetrics
            },
            Warnings = warnings
        };
    }

    private static int GetTextLength(
        IReadOnlyList<StructuredFinancialMetricsExtractedPage> pages)
    {
        return pages.Sum(page => page.Text.Count(character => !char.IsWhiteSpace(character)));
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
            var documentId = Path.GetFileNameWithoutExtension(request.OriginalFileName.Trim());

            if (!string.IsNullOrWhiteSpace(documentId))
            {
                return documentId;
            }
        }

        return "pdf-report";
    }
}
