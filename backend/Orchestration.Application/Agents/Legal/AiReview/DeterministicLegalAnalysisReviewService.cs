using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Legal.Regulations;

namespace Orchestration.Application.Agents.Legal.AiReview;

public sealed class DeterministicLegalAnalysisReviewService : ILegalAnalysisReviewService
{
    private static readonly string[] StandardLimitations =
    {
        "Esta revisión es determinista y asesora.",
        "Este sistema no brinda asesoramiento legal.",
        "No se proporciona ninguna conclusión legal o regulatoria definitiva.",
        "La revisión usa únicamente el análisis financiero y la evidencia CNV/Infoleg provistos.",
        "Se requiere revisión legal humana antes de tomar cualquier determinación legal."
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
        var reviewAreas = BuildReviewAreas(riskSignals, citedEvidence, input.EvidenceAssessment);

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
            return "Posible área de revisión de liquidez/divulgación";
        }
        if (ContainsAny("leverage", "debt", "indebtedness", "solvency", "gearing", "interest_coverage"))
        {
            return "Posible área de revisión de apalancamiento o endeudamiento";
        }
        if (ContainsAny("margin", "profitability", "deterioration", "ebitda", "net_income", "gross_profit", "return"))
        {
            return "Posible área de revisión de desempeño financiero";
        }
        if (ContainsAny("quality", "missing", "data_quality", "reporting_quality", "metric_issue"))
        {
            return "Posible área de revisión de calidad de reporte";
        }

        return "Posible área de revisión de riesgo financiero";
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
        IReadOnlyList<LegalEvidenceReference> citedEvidence,
        RegulatoryEvidenceAssessment? evidenceAssessment)
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

                var severity = evidenceAssessment is null
                    ? DetermineSeverity(signalsInGroup)
                    : NormalizeAssessmentSeverity(evidenceAssessment);

                return new PossibleRegulatoryReviewArea(
                    Title: title,
                    Description: "Las señales de riesgo financiero pueden requerir revisión humana contra la evidencia CNV/Infoleg citada. Esto no es una conclusión legal.",
                    Severity: severity,
                    RelatedFinancialSignals: signalNames,
                    EvidenceCitations: citations
                );
            })
            .ToArray();

        return groupedSignals;
    }

    private static string NormalizeAssessmentSeverity(RegulatoryEvidenceAssessment assessment)
    {
        return string.Equals(assessment.Severity, "Info", StringComparison.OrdinalIgnoreCase)
            ? "Info"
            : "Warning";
    }

    private static string BuildReviewSummary(
        IReadOnlyList<FinancialRiskSignal> riskSignals,
        IReadOnlyList<LegalEvidenceReference> citedEvidence)
    {
        if (riskSignals.Count == 0)
        {
            return "No se identificaron posibles áreas de revisión regulatoria porque no se proporcionaron señales de riesgo financiero. Se recomienda revisión legal humana antes de extraer cualquier conclusión.";
        }

        if (citedEvidence.Count == 0)
        {
            return "La evidencia disponible es limitada. No había evidencia CNV/Infoleg citada para respaldar áreas de revisión regulatoria. Se recomienda revisión legal humana antes de extraer cualquier conclusión.";
        }

        return "El respaldo de revisión legal identificó posibles áreas de revisión regulatoria a partir de señales de riesgo financiero y evidencia CNV/Infoleg provista. Se recomienda revisión legal humana antes de extraer cualquier conclusión.";
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
            warnings.Add("No se proporcionaron señales de riesgo financiero.");
        }

        if (cnvEvidence.Count == 0)
        {
            warnings.Add("No se proporcionó evidencia CNV/Infoleg.");
        }

        if (hasIgnoredEvidence)
        {
            warnings.Add("Se ignoró parte de la evidencia CNV/Infoleg porque no tenía cita.");
        }

        if (riskSignals.Count > 0 && citedEvidence.Count == 0)
        {
            warnings.Add("Había señales de riesgo financiero, pero no había evidencia CNV/Infoleg citada disponible.");
        }

        if (input.FinancialAiReview == null)
        {
            warnings.Add("La revisión de IA de DataAgent no estaba disponible.");
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
