namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record StructuredFinancialMetricsContext(
    string DocumentId,
    string? Company,
    string? Currency,
    string? Unit,
    IReadOnlyList<FinancialMetric> Metrics,
    IReadOnlyList<FinancialMetricsValidationIssue> ValidationWarnings,
    DateTimeOffset UploadedAt
);
