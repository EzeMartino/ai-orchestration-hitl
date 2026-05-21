namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record StructuredFinancialMetricsDocument(
    string DocumentId,
    string Company,
    string Currency,
    string Unit,
    IReadOnlyList<FinancialMetric> Metrics,
    string InputSource = FinancialMetricsInputSources.Unknown,
    StructuredFinancialMetricsProvenance? Provenance = null
);
