namespace Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;

public sealed class DeterministicDataAgentAiReviewService : IDataAgentAiReviewService
{
    private const string DeterministicAdvisoryLimitation = "Esta revisión es determinista y asesora.";
    private const string NoMetricRecalculationLimitation = "No recalcula métricas financieras.";
    private const string NoAccountingVerificationLimitation = "No verifica registros contables.";
    private const string NoRecommendationLimitation = "No brinda recomendaciones de cartera ni de transacciones.";

    public Task<FinancialAnalysisAiReviewResult> ReviewAsync(
        FinancialAnalysisAiReviewInput input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var riskSignals = input.RiskSignals ?? [];
        var warnings = input.Warnings ?? [];
        var inputLimitations = input.Limitations ?? [];
        var notes = BuildDataQualityNotes(input, warnings, inputLimitations);
        var limitations = BuildLimitations(inputLimitations);

        var result = new FinancialAnalysisAiReviewResult(
            Summary: BuildSummary(input, riskSignals),
            KeyFindings: BuildKeyFindings(riskSignals),
            RiskInterpretation: BuildRiskInterpretation(riskSignals, warnings, inputLimitations),
            DataQualityNotes: notes,
            Limitations: limitations,
            UsedLlm: false,
            UsedFallback: true,
            Provider: null,
            Model: null,
            FailureReason: null
        );

        return Task.FromResult(result);
    }

    private static string BuildSummary(
        FinancialAnalysisAiReviewInput input,
        IReadOnlyList<FinancialRiskSignal> riskSignals)
    {
        var signalPhrase = riskSignals.Count == 0
            ? "El análisis financiero no identificó señales de riesgo relevantes a partir de las métricas estructuradas provistas."
            : $"El análisis financiero identificó {riskSignals.Count} señal(es) de riesgo a partir de métricas financieras estructuradas.";

        return GetMetricsSource(input.MetricsInputSource) switch
        {
            FinancialMetricsInputSources.SessionContext =>
                $"{signalPhrase} La revisión usó métricas adjuntas a la sesión de análisis.",
            FinancialMetricsInputSources.FixtureFallback =>
                $"{signalPhrase} La revisión usó métricas fixture de respaldo previstas para demo/desarrollo.",
            FinancialMetricsInputSources.None =>
                "No había métricas financieras estructuradas disponibles para la revisión.",
            _ => signalPhrase
        };
    }

    private static IReadOnlyList<FinancialAnalysisAiKeyFinding> BuildKeyFindings(
        IReadOnlyList<FinancialRiskSignal> riskSignals)
    {
        return riskSignals
            .Select(signal => new FinancialAnalysisAiKeyFinding(
                Title: string.IsNullOrWhiteSpace(signal.Name)
                    ? "Señal de riesgo financiero"
                    : signal.Name,
                Description: BuildFindingDescription(signal),
                Severity: string.IsNullOrWhiteSpace(signal.Severity)
                    ? "Info"
                    : signal.Severity,
                RelatedMetrics: ExtractRelatedMetrics(signal)
            ))
            .ToArray();
    }

    private static string BuildFindingDescription(FinancialRiskSignal signal)
    {
        if (!string.IsNullOrWhiteSpace(signal.Reason))
        {
            return signal.Reason;
        }

        if (!string.IsNullOrWhiteSpace(signal.Summary))
        {
            return signal.Summary;
        }

        return string.IsNullOrWhiteSpace(signal.Period)
            ? "El análisis determinista produjo una señal de riesgo financiero."
            : $"Se produjo una señal de riesgo financiero para {signal.Period}.";
    }

    private static IReadOnlyList<string> ExtractRelatedMetrics(FinancialRiskSignal signal)
    {
        return (signal.Evidence ?? [])
            .Select(evidence => evidence.MetricName)
            .Append(signal.Metric)
            .Where(metric => !string.IsNullOrWhiteSpace(metric))
            .Select(metric => metric!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildRiskInterpretation(
        IReadOnlyList<FinancialRiskSignal> riskSignals,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> limitations)
    {
        var interpretation = riskSignals.Any(signal => IsSeverity(signal.Severity, "High"))
            ? "El análisis incluye señales de riesgo de severidad alta. La revisión humana debe enfocarse en las métricas y evidencias subyacentes."
            : riskSignals.Any(signal => IsSeverity(signal.Severity, "Medium"))
                ? "El análisis incluye indicadores de riesgo moderado que deben revisarse en contexto."
                : riskSignals.Count == 0
                    ? "El análisis financiero determinista no produjo señales de riesgo material."
                    : "El análisis incluye indicadores de riesgo de severidad baja que deben revisarse en contexto.";

        return warnings.Count > 0 || limitations.Count > 0
            ? $"{interpretation} La interpretación debe leerse junto con las advertencias de validación y las limitaciones."
            : interpretation;
    }

    private static IReadOnlyList<FinancialAnalysisAiDataQualityNote> BuildDataQualityNotes(
        FinancialAnalysisAiReviewInput input,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> limitations)
    {
        var notes = new List<FinancialAnalysisAiDataQualityNote>();

        foreach (var warning in warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)))
        {
            notes.Add(new FinancialAnalysisAiDataQualityNote(
                Message: warning,
                Severity: "Warning",
                RelatedFields: []
            ));
        }

        foreach (var limitation in limitations.Where(limitation => !string.IsNullOrWhiteSpace(limitation)))
        {
            notes.Add(new FinancialAnalysisAiDataQualityNote(
                Message: limitation,
                Severity: "Info",
                RelatedFields: ["limitations"]
            ));
        }

        if (GetMetricsSource(input.MetricsInputSource) == FinancialMetricsInputSources.FixtureFallback)
        {
            notes.Add(new FinancialAnalysisAiDataQualityNote(
                Message: "Se usaron métricas fixture de respaldo. Esto está previsto solo para demo/desarrollo.",
                Severity: "Warning",
                RelatedFields: ["metricsInputSource"]
            ));
        }

        if (GetMetricsSource(input.MetricsInputSource) == FinancialMetricsInputSources.None)
        {
            notes.Add(new FinancialAnalysisAiDataQualityNote(
                Message: "No había métricas financieras estructuradas disponibles para la revisión.",
                Severity: "Warning",
                RelatedFields: ["metricsInputSource"]
            ));
        }

        if (input.MetricsProvenance is { WarningCount: > 0 } provenance)
        {
            notes.Add(new FinancialAnalysisAiDataQualityNote(
                Message: $"La ingesta de métricas estructuradas produjo {provenance.WarningCount} advertencia(s).",
                Severity: "Warning",
                RelatedFields: ["metricsProvenance.warningCount"]
            ));
        }

        return notes
            .DistinctBy(note => (note.Message, note.Severity))
            .ToArray();
    }

    private static IReadOnlyList<string> BuildLimitations(IReadOnlyList<string> inputLimitations)
    {
        return inputLimitations
            .Concat(
            [
                DeterministicAdvisoryLimitation,
                NoMetricRecalculationLimitation,
                NoAccountingVerificationLimitation,
                NoRecommendationLimitation
            ])
            .Where(limitation => !string.IsNullOrWhiteSpace(limitation))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetMetricsSource(string? metricsInputSource)
    {
        return string.IsNullOrWhiteSpace(metricsInputSource)
            ? FinancialMetricsInputSources.Unknown
            : metricsInputSource;
    }

    private static bool IsSeverity(string? actual, string expected)
    {
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }
}
