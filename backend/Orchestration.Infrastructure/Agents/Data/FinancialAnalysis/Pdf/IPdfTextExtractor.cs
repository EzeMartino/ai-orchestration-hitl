using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;

public interface IPdfTextExtractor
{
    Task<IReadOnlyList<StructuredFinancialMetricsExtractedPage>> ExtractTextAsync(
        Stream pdf,
        int maxPages,
        CancellationToken cancellationToken);
}
