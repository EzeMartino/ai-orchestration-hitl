using System.Collections.Generic;
using Orchestration.Application.FinancialAnalysis.Thresholds;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record DetectFinancialRiskSignalsRequest(
    IReadOnlyList<FinancialMetric> Metrics,
    IReadOnlyList<FinancialRatio> Ratios,
    IReadOnlyList<FinancialPeriodComparison> Comparisons,
    string? ThresholdProfileName = null,
    IReadOnlyList<FinancialRiskThreshold>? Thresholds = null
);

