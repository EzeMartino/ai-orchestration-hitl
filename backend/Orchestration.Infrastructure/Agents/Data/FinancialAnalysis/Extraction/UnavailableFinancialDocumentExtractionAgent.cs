using Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Extraction;

public sealed class UnavailableFinancialDocumentExtractionAgent :
    IFinancialDocumentExtractionAgent
{
    public Task<FinancialDocumentExtractionParseResult> ExtractAsync(
        FinancialDocumentExtractionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            new FinancialDocumentExtractionParseResult(
                Succeeded: false,
                Result: null,
                FailureReason: "semantic_extraction_unavailable"));
    }
}
