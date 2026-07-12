namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public sealed record FinancialMetricsExtractionDecision(
    bool RequiresSemanticFallback,
    IReadOnlyList<string> ReasonCodes);
