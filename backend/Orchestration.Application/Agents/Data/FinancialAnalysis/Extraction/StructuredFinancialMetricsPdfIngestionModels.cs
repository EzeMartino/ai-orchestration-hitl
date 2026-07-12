using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public enum FinancialMetricsFileOutcome
{
    Accepted,
    ReviewRequired,
    Failed
}

public sealed record StructuredFinancialMetricsPdfIngestionRequest(
    Guid SessionId,
    Guid UserId,
    byte[] PdfBytes,
    string DocumentId,
    string? Company,
    string? Currency,
    string? Unit,
    string OriginalFileName,
    long FileSizeBytes,
    string ContentHash);

public sealed record StructuredFinancialMetricsPdfIngestionResult(
    FinancialMetricsFileOutcome Outcome,
    FinancialMetricsSessionSaveResult? SaveResult,
    FinancialMetricsExtractionDraftDto? ReviewDraft,
    IReadOnlyList<FinancialMetricsValidationIssue> Errors,
    IReadOnlyList<FinancialMetricsValidationIssue> Warnings);
