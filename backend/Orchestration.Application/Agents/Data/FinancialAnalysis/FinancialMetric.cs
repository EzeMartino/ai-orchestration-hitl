namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record FinancialMetric(
    string Name,
    string Period,
    decimal Value,
    string Unit,
    string Statement,
    string? Source,
    string? Currency = null,
    int? SourcePage = null,
    decimal? Confidence = null
);
