namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public interface IFinancialDocumentExtractionAgent
{
    Task<FinancialDocumentExtractionParseResult> ExtractAsync(
        FinancialDocumentExtractionRequest request,
        CancellationToken cancellationToken);
}
