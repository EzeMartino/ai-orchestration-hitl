namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record SaveStructuredFinancialMetricsRequest(
    Guid SessionId,
    StructuredFinancialMetricsInput Input,
    StructuredFinancialMetricsProvenanceInput? Provenance
);

