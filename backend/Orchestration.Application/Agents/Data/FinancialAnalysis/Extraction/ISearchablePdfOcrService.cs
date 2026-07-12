namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public sealed record SearchablePdfOcrResult(
    bool Succeeded,
    byte[] PdfBytes,
    string? FailureReason);

public interface ISearchablePdfOcrService
{
    Task<SearchablePdfOcrResult> CreateSearchablePdfAsync(
        Stream pdf,
        StructuredFinancialMetricsPdfExtractionOptions options,
        CancellationToken cancellationToken);
}
