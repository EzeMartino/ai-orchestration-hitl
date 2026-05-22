namespace Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;

public sealed record FinancialAnalysisAiReviewInput(
    string SessionId,
    string DocumentId,
    string? Company,
    string? MetricsInputSource,
    StructuredFinancialMetricsProvenance? MetricsProvenance,
    IReadOnlyList<FinancialRatio> Ratios,
    IReadOnlyList<FinancialPeriodComparison> PeriodComparisons,
    IReadOnlyList<FinancialRiskSignal> RiskSignals,
    IReadOnlyList<RiskEvidenceItem> RiskEvidence,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Limitations
);
