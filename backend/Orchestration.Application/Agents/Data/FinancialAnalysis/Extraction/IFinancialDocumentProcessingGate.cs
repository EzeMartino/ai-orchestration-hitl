namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public interface IFinancialDocumentProcessingGate
{
    ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken);
}
