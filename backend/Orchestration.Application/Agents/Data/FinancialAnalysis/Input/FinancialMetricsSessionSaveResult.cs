using Orchestration.Application.Agents.Shared;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record FinancialMetricsSessionSaveResult(
    Guid SessionId,
    bool IsValid,
    StructuredFinancialMetricsContext? Context,
    IReadOnlyList<FinancialMetricsValidationIssue> Errors,
    IReadOnlyList<FinancialMetricsValidationIssue> Warnings,
    FinancialReportSummary? ReportSummary = null
);
