using System;
using System.Collections.Generic;

namespace Orchestration.Application.FinancialAnalysis.Thresholds;

public sealed class InMemoryFinancialRiskThresholdProfileProvider : IFinancialRiskThresholdProfileProvider
{
    private static readonly FinancialRiskThresholdProfile DefaultProfile = new(
        Name: "default",
        Description: "Umbrales moderados estándar para la evaluación general de riesgo crediticio corporativo.",
        Thresholds: new[]
        {
            new FinancialRiskThreshold("LOW_CURRENT_RATIO", "current_ratio", "<", 1.2m, "Medium", "El índice de liquidez corriente es menor a 1.2. Se recomienda revisión humana."),
            new FinancialRiskThreshold("LOW_QUICK_RATIO", "quick_ratio", "<", 1.0m, "Medium", "La prueba del ácido (quick ratio) es menor a 1.0. Se debe revisar la liquidez."),
            new FinancialRiskThreshold("HIGH_NET_DEBT_TO_EBITDA", "net_debt_to_ebitda", ">=", 3.0m, "High", "La relación deuda neta a EBITDA supera el umbral de riesgo configurado."),
            new FinancialRiskThreshold("HIGH_DEBT_TO_EQUITY", "debt_to_equity", ">=", 2.0m, "High", "La relación deuda/patrimonio neto supera el umbral de riesgo configurado."),
            new FinancialRiskThreshold("LOW_INTEREST_COVERAGE", "interest_coverage", "<", 2.5m, "High", "La cobertura de intereses está por debajo del umbral de riesgo configurado.")
        }
    );

    private static readonly FinancialRiskThresholdProfile OilAndGasProfile = new(
        Name: "oil_and_gas",
        Description: "Pautas de riesgo específicas del sector para corporaciones de extracción de energía y materias primas.",
        Thresholds: new[]
        {
            new FinancialRiskThreshold("LOW_CURRENT_RATIO", "current_ratio", "<", 1.0m, "Medium", "El índice de liquidez corriente es menor a 1.0. Se recomienda revisión humana."),
            new FinancialRiskThreshold("LOW_QUICK_RATIO", "quick_ratio", "<", 0.8m, "Medium", "La prueba del ácido (quick ratio) es menor a 0.8. Se debe revisar la liquidez."),
            new FinancialRiskThreshold("HIGH_NET_DEBT_TO_EBITDA", "net_debt_to_ebitda", ">=", 3.0m, "High", "La relación deuda neta a EBITDA supera el umbral de riesgo configurado."),
            new FinancialRiskThreshold("HIGH_DEBT_TO_EQUITY", "debt_to_equity", ">=", 2.0m, "High", "La relación deuda/patrimonio neto supera el umbral de riesgo configurado."),
            new FinancialRiskThreshold("LOW_INTEREST_COVERAGE", "interest_coverage", "<", 2.0m, "High", "La cobertura de intereses está por debajo del umbral de riesgo configurado.")
        }
    );

    private static readonly FinancialRiskThresholdProfile StrictProfile = new(
        Name: "strict",
        Description: "Configuraciones de riesgo conservadoras que exigen niveles de apalancamiento bajos y alta liquidez.",
        Thresholds: new[]
        {
            new FinancialRiskThreshold("LOW_CURRENT_RATIO", "current_ratio", "<", 1.5m, "Medium", "El índice de liquidez corriente es menor a 1.5. Se recomienda revisión humana."),
            new FinancialRiskThreshold("LOW_QUICK_RATIO", "quick_ratio", "<", 1.2m, "Medium", "La prueba del ácido (quick ratio) es menor a 1.2. Se debe revisar la liquidez."),
            new FinancialRiskThreshold("HIGH_NET_DEBT_TO_EBITDA", "net_debt_to_ebitda", ">=", 2.5m, "High", "La relación deuda neta a EBITDA supera el umbral de riesgo configurado."),
            new FinancialRiskThreshold("HIGH_DEBT_TO_EQUITY", "debt_to_equity", ">=", 1.5m, "High", "La relación deuda/patrimonio neto supera el umbral de riesgo configurado."),
            new FinancialRiskThreshold("LOW_INTEREST_COVERAGE", "interest_coverage", "<", 3.0m, "High", "La cobertura de intereses está por debajo del umbral de riesgo configurado.")
        }
    );

    private static readonly FinancialRiskThresholdProfile DemoProfile = new(
        Name: "demo",
        Description: "Umbrales sensibles y agresivos adaptados específicamente para demostraciones y pruebas.",
        Thresholds: new[]
        {
            new FinancialRiskThreshold("LOW_CURRENT_RATIO", "current_ratio", "<", 3.0m, "Medium", "El índice de liquidez corriente es menor a 3.0. Se recomienda revisión humana."),
            new FinancialRiskThreshold("LOW_QUICK_RATIO", "quick_ratio", "<", 2.5m, "Medium", "La prueba del ácido (quick ratio) es menor a 2.5. Se debe revisar la liquidez."),
            new FinancialRiskThreshold("HIGH_NET_DEBT_TO_EBITDA", "net_debt_to_ebitda", ">=", 1.0m, "High", "La relación deuda neta a EBITDA supera el umbral de riesgo configurado."),
            new FinancialRiskThreshold("HIGH_DEBT_TO_EQUITY", "debt_to_equity", ">=", 0.5m, "High", "La relación deuda/patrimonio neto supera el umbral de riesgo configurado."),
            new FinancialRiskThreshold("LOW_INTEREST_COVERAGE", "interest_coverage", "<", 5.0m, "High", "La cobertura de intereses está por debajo del umbral de riesgo configurado.")
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
                    Warnings: new[] { $"No se encontró el perfil de umbral solicitado '{profileName}'. Se utilizó el perfil predeterminado ('default')." }
                );
        }
    }
}
