using System;
using System.Collections.Generic;
using System.Linq;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Application.Agents.Legal.Cnv;

public sealed class FinancialAnalysisLegalCnvQueryStrategy : ILegalCnvQueryStrategy
{
    private const int MaxQueries = 4;
    private const string FallbackQueryString = "régimen informativo estados financieros emisoras";

    public IReadOnlyList<LegalCnvQuery> BuildQueries(FinancialAnalysisContext? financialAnalysis)
    {
        if (financialAnalysis == null || financialAnalysis.RiskSignals == null || financialAnalysis.RiskSignals.Count == 0)
        {
            return new List<LegalCnvQuery>
            {
                new(
                    Query: FallbackQueryString,
                    RegulationArea: "general_reporting",
                    Reason: "No specific financial risk signals were available; using a general financial reporting query.",
                    RelatedFinancialSignals: Array.Empty<string>()
                )
            };
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

        // Tarea 3 / 2 (warnings / limitations fallback metrics)
        // If we still have capacity, and there are warnings/limitations or metrics provenance issues, we can add the quality queries
        if (queryList.Count < MaxQueries &&
            (financialAnalysis.Warnings?.Count > 0 || financialAnalysis.Limitations?.Count > 0))
        {
            var qualityCandidates = new[]
            {
                ("deberes informativos emisoras información periódica", "data_quality", "Derived from missing metrics, data quality issues, or warnings."),
                ("régimen informativo estados financieros emisoras", "data_quality", "Derived from missing metrics, data quality issues, or warnings.")
            };

            foreach (var (candQuery, candArea, candReason) in qualityCandidates)
            {
                var normalized = NormalizeQuery(candQuery);
                if (string.IsNullOrEmpty(normalized))
                    continue;

                if (queryDetails.TryGetValue(normalized, out var existing))
                {
                    if (!existing.RelatedSignals.Contains("warning"))
                    {
                        existing.RelatedSignals.Add("warning");
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
                            RelatedSignals: new List<string> { "warning" }
                        );
                    }
                }
            }
        }

        // If for some reason we still have nothing (e.g. risk signals exist but didn't match any category), return fallback
        if (queryList.Count == 0)
        {
            return new List<LegalCnvQuery>
            {
                new(
                    Query: FallbackQueryString,
                    RegulationArea: "general_reporting",
                    Reason: "No specific financial risk signals were available; using a general financial reporting query.",
                    RelatedFinancialSignals: Array.Empty<string>()
                )
            };
        }

        return queryList
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
            .ToList();
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
                $"Derived from high severity signal: {signal.Summary}"
            );
        }

        // 2. Liquidity
        if (signalText.Contains("liquidity") || signalText.Contains("current_ratio") || signalText.Contains("working_capital"))
        {
            yield return (
                "régimen informativo estados financieros liquidez",
                "financial_reporting",
                $"Derived from liquidity risk signal: {signal.Summary}"
            );
            yield return (
                "información financiera periódica estados contables",
                "financial_reporting",
                $"Derived from liquidity risk signal: {signal.Summary}"
            );
        }

        // 3. Leverage
        if (signalText.Contains("leverage") || signalText.Contains("debt") || signalText.Contains("net_debt") || signalText.Contains("indebtedness"))
        {
            yield return (
                "endeudamiento información al mercado estados financieros",
                "debt_leverage",
                $"Derived from leverage risk signal: {signal.Summary}"
            );
            yield return (
                "obligaciones negociables endeudamiento régimen informativo",
                "debt_leverage",
                $"Derived from leverage risk signal: {signal.Summary}"
            );
        }

        // 4. Margin / Profitability
        if (signalText.Contains("margin") || signalText.Contains("profitability") || signalText.Contains("ebitda") || signalText.Contains("gross_margin") || signalText.Contains("deterioration"))
        {
            yield return (
                "resultados estados financieros información periódica emisoras",
                "profitability",
                $"Derived from margin/profitability risk signal: {signal.Summary}"
            );
            yield return (
                "hecho relevante deterioro resultados información al mercado",
                "profitability",
                $"Derived from margin/profitability risk signal: {signal.Summary}"
            );
        }

        // 5. Cash Flow
        if (signalText.Contains("cash_flow") || signalText.Contains("free_cash_flow"))
        {
            yield return (
                "flujo de fondos estados financieros régimen informativo",
                "cash_flow",
                $"Derived from cash flow risk signal: {signal.Summary}"
            );
            yield return (
                "información financiera periódica emisoras",
                "cash_flow",
                $"Derived from cash flow risk signal: {signal.Summary}"
            );
        }

        // 6. Missing Metrics / Data Quality / Warnings
        if (signalText.Contains("missing metrics") || signalText.Contains("data quality") || signalText.Contains("warning"))
        {
            yield return (
                "deberes informativos emisoras información periódica",
                "data_quality",
                $"Derived from missing metrics, data quality issues, or warnings: {signal.Summary}"
            );
            yield return (
                "régimen informativo estados financieros emisoras",
                "data_quality",
                $"Derived from missing metrics, data quality issues, or warnings: {signal.Summary}"
            );
        }
    }
}
