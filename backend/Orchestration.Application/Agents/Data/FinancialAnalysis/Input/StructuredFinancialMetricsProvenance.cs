namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record StructuredFinancialMetricsProvenance(
    string IngestionMethod,
    string? OriginalFileName,
    long? FileSizeBytes,
    string? ContentHash,
    int MetricCount,
    int WarningCount
);

