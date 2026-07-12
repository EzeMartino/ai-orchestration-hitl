namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public interface IFinancialMetricsExtractionDraftService
{
    Task<FinancialMetricsExtractionDraftServiceResult> CreateOrReplaceAsync(
        Guid sessionId,
        Guid userId,
        CreateFinancialMetricsExtractionDraftRequest request,
        CancellationToken cancellationToken);

    Task<FinancialMetricsExtractionDraftServiceResult> GetPendingAsync(
        Guid sessionId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<FinancialMetricsExtractionDraftServiceResult> UpdateAsync(
        Guid draftId,
        Guid sessionId,
        Guid userId,
        UpdateFinancialMetricsExtractionDraftRequest request,
        CancellationToken cancellationToken);

    Task<FinancialMetricsExtractionDraftServiceResult> ConfirmAsync(
        Guid draftId,
        Guid sessionId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<FinancialMetricsExtractionDraftServiceResult> DiscardAsync(
        Guid draftId,
        Guid sessionId,
        Guid userId,
        CancellationToken cancellationToken);
}
