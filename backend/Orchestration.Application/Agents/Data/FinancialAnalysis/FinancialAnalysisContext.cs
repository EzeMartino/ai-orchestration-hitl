using System.Collections.Generic;
using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;
using Orchestration.Application.FinancialAnalysis.Thresholds;

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
    FinancialAnalysisAiReviewResult? AiReview = null,
    string? ThresholdProfile = null,
    IReadOnlyList<FinancialRiskThreshold>? ThresholdsUsed = null
)
{
    public IReadOnlyList<FinancialRiskThreshold> ThresholdsUsed { get; init; } = ThresholdsUsed ?? [];

    public FinancialAnalysisExecution Execution { get; init; } =
        FinancialAnalysisExecution.LegacyUnknown;
}
