using System.Collections.Generic;

namespace Orchestration.Application.FinancialAnalysis.Thresholds;

public sealed record FinancialRiskThresholdProfileResolution(
    string? RequestedProfile,
    FinancialRiskThresholdProfile Profile,
    bool UsedFallback,
    IReadOnlyList<string> Warnings
);
