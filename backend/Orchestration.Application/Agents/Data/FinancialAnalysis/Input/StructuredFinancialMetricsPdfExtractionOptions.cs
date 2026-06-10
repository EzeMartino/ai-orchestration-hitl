namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsPdfExtractionOptions
{
    public const string SectionName = "StructuredFinancialMetricsPdfExtraction";

    public int NativeTextMinimumCharacters { get; init; } = 200;

    public int MaxPages { get; init; } = 20;

    public int OcrDpi { get; init; } = 200;

    public int OcrTimeoutSeconds { get; init; } = 60;

    public long MaxTemporaryBytes { get; init; } = 536_870_912;

    public long MaxSearchablePdfBytes { get; init; } = 104_857_600;

    public decimal MinimumMetricConfidence { get; init; } = 0.5m;

    public string PdfToPpmPath { get; init; } = "pdftoppm";

    public string TesseractPath { get; init; } = "tesseract";

    public string TesseractLanguage { get; init; } = "eng";
}
