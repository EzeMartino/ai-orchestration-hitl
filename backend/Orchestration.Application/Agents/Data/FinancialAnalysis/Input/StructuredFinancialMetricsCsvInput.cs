using Orchestration.Application.Agents.Shared;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record StructuredFinancialMetricsCsvInput(
    string DocumentId,
    string? Company,
    string? Currency,
    string? Unit,
    string Csv,
    FinancialReportSummaryInput? ReportSummary = null
);
