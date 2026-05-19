namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record FinancialMetricsValidationResult(
    bool IsValid,
    IReadOnlyList<ValidatedFinancialMetric> Metrics,
    IReadOnlyList<FinancialMetricsValidationIssue> Errors,
    IReadOnlyList<FinancialMetricsValidationIssue> Warnings
);

public sealed record ValidatedFinancialMetric(
    string Name,
    string Period,
    decimal Value,
    string Unit,
    string? Currency,
    string Source,
    int? SourcePage,
    decimal Confidence
);

public sealed record FinancialMetricsValidationIssue(
    string Code,
    string Message,
    string? MetricName,
    string? Period,
    string Severity
);
