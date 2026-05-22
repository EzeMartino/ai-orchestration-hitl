using System.Collections.Generic;

namespace Orchestration.Application.FinancialAnalysis.Thresholds;

public sealed record FinancialRiskThresholdProfile(
    string Name,
    string Description,
    IReadOnlyList<FinancialRiskThreshold> Thresholds
);
