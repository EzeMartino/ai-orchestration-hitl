namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record StructuredFinancialMetricsProvenanceInput(
    string IngestionMethod,
    string? OriginalFileName,
    long? FileSizeBytes,
    string? ContentHash
);

