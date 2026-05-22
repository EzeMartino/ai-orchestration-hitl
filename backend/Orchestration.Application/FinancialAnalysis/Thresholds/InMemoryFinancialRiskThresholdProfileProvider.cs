using System;
using System.Collections.Generic;

namespace Orchestration.Application.FinancialAnalysis.Thresholds;

public sealed class InMemoryFinancialRiskThresholdProfileProvider : IFinancialRiskThresholdProfileProvider
{
    private static readonly FinancialRiskThresholdProfile DefaultProfile = new(
        Name: "default",
        Description: "Standard moderate thresholds for general corporate credit risk evaluation.",
        Thresholds: new[]
        {
            new FinancialRiskThreshold("LOW_CURRENT_RATIO", "current_ratio", "<", 1.2m, "Medium", "Current ratio is below 1.2. Human review recommended."),
            new FinancialRiskThreshold("LOW_QUICK_RATIO", "quick_ratio", "<", 1.0m, "Medium", "Quick ratio is below 1.0. Liquidity should be reviewed."),
            new FinancialRiskThreshold("HIGH_NET_DEBT_TO_EBITDA", "net_debt_to_ebitda", ">=", 3.0m, "High", "Net debt to EBITDA is above the configured risk threshold."),
            new FinancialRiskThreshold("HIGH_DEBT_TO_EQUITY", "debt_to_equity", ">=", 2.0m, "High", "Debt to equity is above the configured risk threshold."),
            new FinancialRiskThreshold("LOW_INTEREST_COVERAGE", "interest_coverage", "<", 2.5m, "High", "Interest coverage is below the configured risk threshold.")
        }
    );

    private static readonly FinancialRiskThresholdProfile OilAndGasProfile = new(
        Name: "oil_and_gas",
        Description: "Industry-specific risk guidelines for energy and commodity extraction corporations.",
        Thresholds: new[]
        {
            new FinancialRiskThreshold("LOW_CURRENT_RATIO", "current_ratio", "<", 1.0m, "Medium", "Current ratio is below 1.0. Human review recommended."),
            new FinancialRiskThreshold("LOW_QUICK_RATIO", "quick_ratio", "<", 0.8m, "Medium", "Quick ratio is below 0.8. Liquidity should be reviewed."),
            new FinancialRiskThreshold("HIGH_NET_DEBT_TO_EBITDA", "net_debt_to_ebitda", ">=", 3.0m, "High", "Net debt to EBITDA is above the configured risk threshold."),
            new FinancialRiskThreshold("HIGH_DEBT_TO_EQUITY", "debt_to_equity", ">=", 2.0m, "High", "Debt to equity is above the configured risk threshold."),
            new FinancialRiskThreshold("LOW_INTEREST_COVERAGE", "interest_coverage", "<", 2.0m, "High", "Interest coverage is below the configured risk threshold.")
        }
    );

    private static readonly FinancialRiskThresholdProfile StrictProfile = new(
        Name: "strict",
        Description: "Conservative risk settings enforcing highly safe liquidity and low leverage levels.",
        Thresholds: new[]
        {
            new FinancialRiskThreshold("LOW_CURRENT_RATIO", "current_ratio", "<", 1.5m, "Medium", "Current ratio is below 1.5. Human review recommended."),
            new FinancialRiskThreshold("LOW_QUICK_RATIO", "quick_ratio", "<", 1.2m, "Medium", "Quick ratio is below 1.2. Liquidity should be reviewed."),
            new FinancialRiskThreshold("HIGH_NET_DEBT_TO_EBITDA", "net_debt_to_ebitda", ">=", 2.5m, "High", "Net debt to EBITDA is above the configured risk threshold."),
            new FinancialRiskThreshold("HIGH_DEBT_TO_EQUITY", "debt_to_equity", ">=", 1.5m, "High", "Debt to equity is above the configured risk threshold."),
            new FinancialRiskThreshold("LOW_INTEREST_COVERAGE", "interest_coverage", "<", 3.0m, "High", "Interest coverage is below the configured risk threshold.")
        }
    );

    private static readonly FinancialRiskThresholdProfile DemoProfile = new(
        Name: "demo",
        Description: "Sensitive and aggressive thresholds tailored specifically for demonstrations and testing.",
        Thresholds: new[]
        {
            new FinancialRiskThreshold("LOW_CURRENT_RATIO", "current_ratio", "<", 3.0m, "Medium", "Current ratio is below 3.0. Human review recommended."),
            new FinancialRiskThreshold("LOW_QUICK_RATIO", "quick_ratio", "<", 2.5m, "Medium", "Quick ratio is below 2.5. Liquidity should be reviewed."),
            new FinancialRiskThreshold("HIGH_NET_DEBT_TO_EBITDA", "net_debt_to_ebitda", ">=", 1.0m, "High", "Net debt to EBITDA is above the configured risk threshold."),
            new FinancialRiskThreshold("HIGH_DEBT_TO_EQUITY", "debt_to_equity", ">=", 0.5m, "High", "Debt to equity is above the configured risk threshold."),
            new FinancialRiskThreshold("LOW_INTEREST_COVERAGE", "interest_coverage", "<", 5.0m, "High", "Interest coverage is below the configured risk threshold.")
        }
    );

    public FinancialRiskThresholdProfileResolution ResolveProfile(string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return new FinancialRiskThresholdProfileResolution(
                RequestedProfile: profileName,
                Profile: DefaultProfile,
                UsedFallback: false,
                Warnings: Array.Empty<string>()
            );
        }

        var normalizedName = profileName.Trim();

        if (string.Equals(normalizedName, "default_oil_and_gas_equity_research", StringComparison.OrdinalIgnoreCase))
        {
            return new FinancialRiskThresholdProfileResolution(
                RequestedProfile: profileName,
                Profile: OilAndGasProfile,
                UsedFallback: false,
                Warnings: Array.Empty<string>()
            );
        }

        switch (normalizedName.ToLowerInvariant())
        {
            case "default":
                return new FinancialRiskThresholdProfileResolution(
                    RequestedProfile: profileName,
                    Profile: DefaultProfile,
                    UsedFallback: false,
                    Warnings: Array.Empty<string>()
                );

            case "oil_and_gas":
                return new FinancialRiskThresholdProfileResolution(
                    RequestedProfile: profileName,
                    Profile: OilAndGasProfile,
                    UsedFallback: false,
                    Warnings: Array.Empty<string>()
                );

            case "strict":
                return new FinancialRiskThresholdProfileResolution(
                    RequestedProfile: profileName,
                    Profile: StrictProfile,
                    UsedFallback: false,
                    Warnings: Array.Empty<string>()
                );

            case "demo":
                return new FinancialRiskThresholdProfileResolution(
                    RequestedProfile: profileName,
                    Profile: DemoProfile,
                    UsedFallback: false,
                    Warnings: Array.Empty<string>()
                );

            default:
                return new FinancialRiskThresholdProfileResolution(
                    RequestedProfile: profileName,
                    Profile: DefaultProfile,
                    UsedFallback: true,
                    Warnings: new[] { $"Requested threshold profile '{profileName}' was not found. Fallen back to 'default'." }
                );
        }
    }
}
