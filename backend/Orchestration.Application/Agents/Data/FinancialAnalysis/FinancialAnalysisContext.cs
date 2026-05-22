using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record FinancialAnalysisContext(
    string Engine,
    string DocumentId,
    string? Company,
    IReadOnlyList<FinancialRatio> Ratios,
    IReadOnlyList<FinancialPeriodComparison> Comparisons,
    IReadOnlyList<FinancialRiskSignal> RiskSignals,
    IReadOnlyList<RiskEvidenceItem> RiskEvidence,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Limitations,
    string MetricsInputSource = FinancialMetricsInputSources.Unknown,
    StructuredFinancialMetricsProvenance? MetricsProvenance = null,
    FinancialAnalysisAiReviewResult? AiReview = null
);
