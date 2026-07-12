namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public interface IStructuredFinancialMetricsPdfIngestionService
{
    Task<StructuredFinancialMetricsPdfIngestionResult> IngestAsync(
        StructuredFinancialMetricsPdfIngestionRequest request,
        CancellationToken cancellationToken = default);
}
