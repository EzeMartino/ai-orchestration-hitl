namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public sealed record FinancialDocumentMarkdownResult(
    bool Succeeded,
    string Markdown,
    bool Truncated,
    string? FailureReason);
