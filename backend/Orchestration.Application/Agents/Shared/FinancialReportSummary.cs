namespace Orchestration.Application.Agents.Shared;

public sealed record FinancialReportSummaryInput(
    string? ReportName,
    decimal? TotalAmount,
    int? TransactionCount,
    DateTimeOffset? SubmittedAt);

public sealed record FinancialReportSummary(
    string ReportName,
    decimal TotalAmount,
    int TransactionCount,
    DateTimeOffset SubmittedAt);
