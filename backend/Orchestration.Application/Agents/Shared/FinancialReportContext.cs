namespace Orchestration.Application.Agents.Shared;

public sealed record FinancialReportContext(
    Guid SessionId,
    string ReportName,
    decimal TotalAmount,
    int TransactionCount,
    DateTimeOffset SubmittedAt
);