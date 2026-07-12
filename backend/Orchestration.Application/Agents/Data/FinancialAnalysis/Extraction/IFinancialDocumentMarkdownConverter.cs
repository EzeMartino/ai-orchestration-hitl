namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public interface IFinancialDocumentMarkdownConverter
{
    Task<FinancialDocumentMarkdownResult> ConvertPdfAsync(
        Stream pdf,
        int maxCharacters,
        int maxPages,
        CancellationToken cancellationToken);
}
