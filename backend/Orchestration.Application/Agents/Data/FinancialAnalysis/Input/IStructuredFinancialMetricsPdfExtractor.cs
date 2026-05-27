namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public interface IStructuredFinancialMetricsPdfExtractor
{
    Task<StructuredFinancialMetricsPdfExtractionResult> ExtractAsync(
        Stream pdf,
        StructuredFinancialMetricsPdfExtractionRequest request,
        CancellationToken cancellationToken);
}
