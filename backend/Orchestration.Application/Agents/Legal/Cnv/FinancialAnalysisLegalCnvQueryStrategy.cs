using System;
using System.Collections.Generic;
using System.Linq;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Application.Agents.Legal.Cnv;

public sealed class FinancialAnalysisLegalCnvQueryStrategy : ILegalCnvQueryStrategy
{
    private const int MaxQueries = 4;
    private const string StrategyVersion = "financial_analysis_v2";
    private const string FallbackQueryString = "régimen informativo estados financieros emisoras";

    public IReadOnlyList<LegalCnvQuery> BuildQueries(FinancialAnalysisContext? financialAnalysis)
    {
        return BuildPlan(
            financialAnalysis,
            LegalDataEvidenceClassifier.Classify(
                LegalDataToolStatuses.Executed,
                financialAnalysis)
        ).Queries;
    }

    public LegalCnvQueryPlan BuildPlan(
        FinancialAnalysisContext? financialAnalysis,
        LegalDataEvidenceContext dataEvidence)
    {
        var canonicalEvidence = LegalDataEvidenceClassifier.Classify(
            dataEvidence?.DataToolStatus!,
            financialAnalysis
        );

        if (dataEvidence is null ||
            !EvidenceIsEquivalent(dataEvidence, canonicalEvidence))
        {
            canonicalEvidence = CreateAmbiguousEvidence(canonicalEvidence);
        }

        if (!canonicalEvidence.CanUseSignals)
        {
            return CreateFallbackPlan(
                canonicalEvidence,
                canonicalEvidence.FallbackReason ??
                    LegalCnvFallbackReasons.LegacyOrAmbiguousExecution
            );
        }

        if (financialAnalysis is null ||
            financialAnalysis.RiskSignals is null ||
            financialAnalysis.RiskSignals.Count == 0)
        {
            return CreateFallbackPlan(
                CreateAmbiguousEvidence(canonicalEvidence),
                LegalCnvFallbackReasons.LegacyOrAmbiguousExecution
            );
        }

        var orderedSignals = financialAnalysis.RiskSignals
            .OrderByDescending(s => IsHighSeverity(s.Severity))
            .ToList();

        var queryList = new List<string>();
        var queryDetails = new Dictionary<string, (string Query, string? RegulationArea, string Reason, List<string> RelatedSignals)>();

        foreach (var signal in orderedSignals)
        {
            var candidates = MapSignalToQueries(signal);
            foreach (var (candQuery, candArea, candReason) in candidates)
            {
                var normalized = NormalizeQuery(candQuery);
                if (string.IsNullOrEmpty(normalized))
                    continue;

                if (queryDetails.TryGetValue(normalized, out var existing))
                {
                    if (!existing.RelatedSignals.Contains(signal.Name))
                    {
                        existing.RelatedSignals.Add(signal.Name);
                    }
                }
                else
                {
                    if (queryList.Count < MaxQueries)
                    {
                        queryList.Add(normalized);
                        queryDetails[normalized] = (
                            Query: candQuery,
                            RegulationArea: candArea,
                            Reason: candReason,
                            RelatedSignals: new List<string> { signal.Name }
                        );
                    }
                }
            }
        }

        if (queryList.Count == 0)
        {
            return CreateFallbackPlan(
                canonicalEvidence,
                LegalCnvFallbackReasons.SignalsUnmapped
            );
        }

        var queries = queryList
            .Select(norm =>
            {
                var details = queryDetails[norm];
                return new LegalCnvQuery(
                    Query: details.Query,
                    RegulationArea: details.RegulationArea,
                    Reason: details.Reason,
                    RelatedFinancialSignals: details.RelatedSignals.AsReadOnly()
                );
            })
            .ToArray();

        return new LegalCnvQueryPlan(
            StrategyVersion,
            LegalCnvQuerySources.Contextual,
            FallbackReason: null,
            canonicalEvidence,
            queries
        );
    }

    private static bool EvidenceIsEquivalent(
        LegalDataEvidenceContext supplied,
        LegalDataEvidenceContext canonical)
    {
        return supplied.CanUseSignals == canonical.CanUseSignals &&
            string.Equals(
                supplied.FallbackReason,
                canonical.FallbackReason,
                StringComparison.Ordinal) &&
            string.Equals(
                supplied.DataToolStatus,
                canonical.DataToolStatus,
                StringComparison.Ordinal) &&
            supplied.FinancialAnalysisStatus == canonical.FinancialAnalysisStatus &&
            supplied.FailedStages is not null &&
            supplied.FailedStages.SequenceEqual(canonical.FailedStages);
    }

    private static LegalDataEvidenceContext CreateAmbiguousEvidence(
        LegalDataEvidenceContext canonical)
    {
        return new LegalDataEvidenceContext(
            CanUseSignals: false,
            FallbackReason: LegalCnvFallbackReasons.LegacyOrAmbiguousExecution,
            DataToolStatus: LegalDataToolStatuses.Unknown,
            FinancialAnalysisStatus: canonical.FinancialAnalysisStatus,
            FailedStages: canonical.FailedStages
        );
    }

    private static LegalCnvQueryPlan CreateFallbackPlan(
        LegalDataEvidenceContext dataEvidence,
        string fallbackReason)
    {
        return new LegalCnvQueryPlan(
            StrategyVersion,
            LegalCnvQuerySources.Fallback,
            fallbackReason,
            dataEvidence,
            [CreateFallbackQuery()]
        );
    }

    private static LegalCnvQuery CreateFallbackQuery()
    {
        return new LegalCnvQuery(
            Query: FallbackQueryString,
            RegulationArea: "general_reporting",
            Reason: "No había señales específicas de riesgo financiero disponibles; se usó una consulta general sobre información financiera.",
            RelatedFinancialSignals: Array.Empty<string>()
        );
    }

    private static bool IsHighSeverity(string? severity)
    {
        if (string.IsNullOrWhiteSpace(severity))
            return false;
        var lower = severity.ToLowerInvariant();
        return lower == "high" || lower == "critical" || lower == "severe";
    }

    private static string NormalizeQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return string.Empty;

        var trimmed = query.Trim().ToLowerInvariant();
        var parts = trimmed.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts);
    }

    private static IEnumerable<(string Query, string RegulationArea, string Reason)> MapSignalToQueries(FinancialRiskSignal signal)
    {
        var signalText = string.Join(
            " ",
            signal.Name,
            signal.Summary,
            signal.Metric,
            signal.ThresholdCode,
            signal.Reason
        ).ToLowerInvariant();
        var severityLower = (signal.Severity ?? string.Empty).ToLowerInvariant();

        // 1. High Severity / Material Deterioration
        if (severityLower == "high" || severityLower == "critical" || severityLower == "severe" ||
            signalText.Contains("material deterioration"))
        {
            yield return (
                "hecho relevante información al mercado emisoras",
                "material_deterioration",
                $"Derivada de señal de severidad alta: {signal.Summary}"
            );
        }

        // 2. Liquidity
        if (signalText.Contains("liquidity") || signalText.Contains("current_ratio") || signalText.Contains("working_capital"))
        {
            yield return (
                "régimen informativo estados financieros liquidez",
                "financial_reporting",
                $"Derivada de señal de riesgo de liquidez: {signal.Summary}"
            );
            yield return (
                "información financiera periódica estados contables",
                "financial_reporting",
                $"Derivada de señal de riesgo de liquidez: {signal.Summary}"
            );
        }

        // 3. Leverage
        if (signalText.Contains("leverage") || signalText.Contains("debt") || signalText.Contains("net_debt") || signalText.Contains("indebtedness"))
        {
            yield return (
                "endeudamiento información al mercado estados financieros",
                "debt_leverage",
                $"Derivada de señal de riesgo de apalancamiento: {signal.Summary}"
            );
            yield return (
                "obligaciones negociables endeudamiento régimen informativo",
                "debt_leverage",
                $"Derivada de señal de riesgo de apalancamiento: {signal.Summary}"
            );
        }

        // 4. Margin / Profitability
        if (signalText.Contains("margin") || signalText.Contains("profitability") || signalText.Contains("ebitda") || signalText.Contains("gross_margin") || signalText.Contains("deterioration"))
        {
            yield return (
                "resultados estados financieros información periódica emisoras",
                "profitability",
                $"Derivada de señal de riesgo de margen/rentabilidad: {signal.Summary}"
            );
            yield return (
                "hecho relevante deterioro resultados información al mercado",
                "profitability",
                $"Derivada de señal de riesgo de margen/rentabilidad: {signal.Summary}"
            );
        }

        // 5. Cash Flow
        if (signalText.Contains("cash_flow") || signalText.Contains("free_cash_flow"))
        {
            yield return (
                "flujo de fondos estados financieros régimen informativo",
                "cash_flow",
                $"Derivada de señal de riesgo de flujo de caja: {signal.Summary}"
            );
            yield return (
                "información financiera periódica emisoras",
                "cash_flow",
                $"Derivada de señal de riesgo de flujo de caja: {signal.Summary}"
            );
        }

        // 6. Missing Metrics / Data Quality / Warnings
        if (signalText.Contains("missing metrics") || signalText.Contains("data quality") || signalText.Contains("warning"))
        {
            yield return (
                "deberes informativos emisoras información periódica",
                "data_quality",
                $"Derivada de métricas faltantes, problemas de calidad de datos o advertencias: {signal.Summary}"
            );
            yield return (
                "régimen informativo estados financieros emisoras",
                "data_quality",
                $"Derivada de métricas faltantes, problemas de calidad de datos o advertencias: {signal.Summary}"
            );
        }
    }
}
