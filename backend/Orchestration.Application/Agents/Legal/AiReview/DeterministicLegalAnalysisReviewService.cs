using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Application.Agents.Legal.AiReview;

public sealed class DeterministicLegalAnalysisReviewService : ILegalAnalysisReviewService
{
    private static readonly string[] StandardLimitations =
    {
        "This review is deterministic and advisory.",
        "This system does not provide legal advice.",
        "No definitive legal or regulatory conclusion is provided.",
        "The review uses only the provided financial analysis and CNV/Infoleg evidence.",
        "Human legal review is required before making any legal determination."
    };

    public Task<LegalAnalysisReviewResult> ReviewAsync(
        LegalAnalysisReviewInput input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var riskSignals = input.FinancialRiskSignals ?? Array.Empty<FinancialRiskSignal>();
        var cnvEvidence = input.CnvEvidence ?? Array.Empty<LegalEvidenceReference>();
        var financialWarnings = input.FinancialWarnings ?? Array.Empty<string>();
        var financialLimitations = input.FinancialLimitations ?? Array.Empty<string>();

        // Tarea 3: Filter usable legal evidence (only with citation)
        var citedEvidence = GetCitedEvidence(cnvEvidence);
        var hasIgnoredEvidence = cnvEvidence.Any(e => string.IsNullOrWhiteSpace(e.Citation));

        // Tarea 4: PossibleRegulatoryReviewAreas determinísticas
        var reviewAreas = BuildReviewAreas(riskSignals, citedEvidence);

        // Tarea 8: Warnings
        var warnings = BuildWarnings(input, riskSignals, cnvEvidence, citedEvidence, hasIgnoredEvidence, financialWarnings);

        // Tarea 9: Limitations
        var limitations = BuildLimitations(financialLimitations);

        // Tarea 2: Resumen determinístico
        var summary = BuildReviewSummary(riskSignals, citedEvidence);

        var result = new LegalAnalysisReviewResult(
            ReviewSummary: summary,
            PossibleRegulatoryReviewAreas: reviewAreas,
            EvidenceReferences: citedEvidence,
            Warnings: warnings,
            Limitations: limitations,
            UsedLlm: false,
            UsedFallback: true,
            Provider: null,
            Model: null,
            FailureReason: null
        );

        return Task.FromResult(result);
    }

    private static IReadOnlyList<LegalEvidenceReference> GetCitedEvidence(
        IReadOnlyList<LegalEvidenceReference> evidence)
    {
        return evidence
            .Where(e => !string.IsNullOrWhiteSpace(e.Citation))
            .ToArray();
    }

    private static string MapSignalToAreaTitle(FinancialRiskSignal signal)
    {
        var textToSearch = string.Join(
            " ",
            signal.Name,
            signal.Summary,
            signal.Metric,
            signal.ThresholdCode,
            signal.Reason
        ).ToLowerInvariant();
        var metrics = (signal.Evidence ?? Array.Empty<RiskEvidenceItem>())
            .Select(e => e.MetricName ?? "")
            .ToList();

        bool ContainsAny(params string[] keywords)
        {
            return keywords.Any(k => textToSearch.Contains(k)) ||
                   metrics.Any(m => keywords.Any(k => m.ToLowerInvariant().Contains(k)));
        }

        if (ContainsAny("liquidity", "current_ratio", "quick_ratio", "cash_ratio"))
        {
            return "Possible liquidity/disclosure review area";
        }
        if (ContainsAny("leverage", "debt", "indebtedness", "solvency", "gearing", "interest_coverage"))
        {
            return "Possible leverage or indebtedness disclosure review area";
        }
        if (ContainsAny("margin", "profitability", "deterioration", "ebitda", "net_income", "gross_profit", "return"))
        {
            return "Possible financial performance disclosure review area";
        }
        if (ContainsAny("quality", "missing", "data_quality", "reporting_quality", "metric_issue"))
        {
            return "Possible reporting quality review area";
        }

        return "Possible financial risk review area";
    }

    private static string DetermineSeverity(IReadOnlyList<FinancialRiskSignal> signals)
    {
        if (signals.Count == 0)
        {
            return "Info";
        }

        var severities = signals
            .Select(s => s.Severity ?? "")
            .Select(sev => sev.Trim())
            .ToList();

        bool HasSeverity(string expected)
        {
            return severities.Any(s => string.Equals(s, expected, StringComparison.OrdinalIgnoreCase));
        }

        if (HasSeverity("High")) return "High";
        if (HasSeverity("Medium")) return "Medium";
        if (HasSeverity("Low")) return "Low";
        if (HasSeverity("Warning")) return "Warning";
        if (HasSeverity("Info")) return "Info";

        return "Info";
    }

    private static IReadOnlyList<PossibleRegulatoryReviewArea> BuildReviewAreas(
        IReadOnlyList<FinancialRiskSignal> riskSignals,
        IReadOnlyList<LegalEvidenceReference> citedEvidence)
    {
        if (riskSignals.Count == 0 || citedEvidence.Count == 0)
        {
            return Array.Empty<PossibleRegulatoryReviewArea>();
        }

        var citations = citedEvidence
            .Select(e => e.Citation)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!)
            .Distinct()
            .ToList();


        // Group signals by their mapped title to avoid duplicates
        var groupedSignals = riskSignals
            .GroupBy(MapSignalToAreaTitle)
            .Select(group =>
            {
                var title = group.Key;
                var signalsInGroup = group.ToList();
                var signalNames = signalsInGroup
                    .Select(s => s.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .ToList();

                var severity = DetermineSeverity(signalsInGroup);

                return new PossibleRegulatoryReviewArea(
                    Title: title,
                    Description: "Financial risk signals may require human review against the cited CNV/Infoleg evidence. This is not a legal conclusion.",
                    Severity: severity,
                    RelatedFinancialSignals: signalNames,
                    EvidenceCitations: citations
                );
            })
            .ToArray();

        return groupedSignals;
    }

    private static string BuildReviewSummary(
        IReadOnlyList<FinancialRiskSignal> riskSignals,
        IReadOnlyList<LegalEvidenceReference> citedEvidence)
    {
        if (riskSignals.Count == 0)
        {
            return "No possible regulatory review areas were identified because no financial risk signals were provided. Human legal review is recommended before drawing any conclusion.";
        }

        if (citedEvidence.Count == 0)
        {
            return "The available evidence is limited. No cited CNV/Infoleg evidence was available to support regulatory review areas. Human legal review is recommended before drawing any conclusion.";
        }

        return "The legal review fallback identified possible regulatory review areas based on financial risk signals and provided CNV/Infoleg evidence. Human legal review is recommended before drawing any conclusion.";
    }

    private static IReadOnlyList<string> BuildWarnings(
        LegalAnalysisReviewInput input,
        IReadOnlyList<FinancialRiskSignal> riskSignals,
        IReadOnlyList<LegalEvidenceReference> cnvEvidence,
        IReadOnlyList<LegalEvidenceReference> citedEvidence,
        bool hasIgnoredEvidence,
        IReadOnlyList<string> financialWarnings)
    {
        var warnings = new List<string>();

        if (riskSignals.Count == 0)
        {
            warnings.Add("No financial risk signals were provided.");
        }

        if (cnvEvidence.Count == 0)
        {
            warnings.Add("No CNV/Infoleg evidence was provided.");
        }

        if (hasIgnoredEvidence)
        {
            warnings.Add("Some CNV/Infoleg evidence was ignored because it had no citation.");
        }

        if (riskSignals.Count > 0 && citedEvidence.Count == 0)
        {
            warnings.Add("Financial risk signals were present, but no cited CNV/Infoleg evidence was available.");
        }

        if (input.FinancialAiReview == null)
        {
            warnings.Add("DataAgent AI review was not available.");
        }

        // Add financial warnings from input
        if (financialWarnings != null)
        {
            foreach (var w in financialWarnings)
            {
                if (!string.IsNullOrWhiteSpace(w))
                {
                    warnings.Add(w);
                }
            }
        }

        return warnings
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildLimitations(
        IReadOnlyList<string> financialLimitations)
    {
        var limitations = new List<string>(StandardLimitations);

        if (financialLimitations != null)
        {
            foreach (var lim in financialLimitations)
            {
                if (!string.IsNullOrWhiteSpace(lim))
                {
                    limitations.Add(lim);
                }
            }
        }

        return limitations
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
