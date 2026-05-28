using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;

public interface IOcrTextExtractor
{
    Task<IReadOnlyList<StructuredFinancialMetricsExtractedPage>> ExtractTextAsync(
        Stream pdf,
        StructuredFinancialMetricsPdfExtractionOptions options,
        CancellationToken cancellationToken);
}

public sealed class PdfOcrDependencyException : Exception
{
    public PdfOcrDependencyException(
        string message)
        : base(message)
    {
    }
}
