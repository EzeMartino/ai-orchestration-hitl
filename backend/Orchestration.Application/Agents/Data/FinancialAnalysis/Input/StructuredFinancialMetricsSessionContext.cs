using Orchestration.Application.Agents.Shared;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record StructuredFinancialMetricsSessionContext(
    StructuredFinancialMetricsContext? Metrics,
    FinancialReportSummary? ReportSummary);
