using Orchestration.Application.Agents.Shared;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record StructuredFinancialMetricsInput(
    string DocumentId,
    string? Company,
    string? Currency,
    string? Unit,
    IReadOnlyList<StructuredFinancialMetricInput> Metrics,
    FinancialReportSummaryInput? ReportSummary = null
);

public sealed record StructuredFinancialMetricInput(
    string Name,
    string Period,
    decimal? Value,
    string? Unit,
    string? Currency,
    string? Source,
    int? SourcePage,
    decimal? Confidence
);
