namespace Orchestration.Application.FinancialAnalysis.Thresholds;

public interface IFinancialRiskThresholdProfileProvider
{
    FinancialRiskThresholdProfileResolution ResolveProfile(string? profileName);
}
