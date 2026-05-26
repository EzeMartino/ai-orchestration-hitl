namespace Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;

public sealed class DeterministicDataAgentAiReviewService : IDataAgentAiReviewService
{
    private const string DeterministicAdvisoryLimitation = "This review is deterministic and advisory.";
    private const string NoMetricRecalculationLimitation = "It does not recompute financial metrics.";
    private const string NoAccountingVerificationLimitation = "It does not verify accounting records.";
    private const string NoRecommendationLimitation = "It does not provide portfolio or transaction recommendations.";

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
            ? "The financial analysis did not identify major risk signals based on the provided structured metrics."
            : $"The financial analysis identified {riskSignals.Count} risk signal(s) based on structured financial metrics.";

        return GetMetricsSource(input.MetricsInputSource) switch
        {
            FinancialMetricsInputSources.SessionContext =>
                $"{signalPhrase} The review used metrics attached to the analysis session.",
            FinancialMetricsInputSources.FixtureFallback =>
                $"{signalPhrase} The review used fixture fallback metrics intended for demo/development use.",
            FinancialMetricsInputSources.None =>
                "Structured financial metrics were not available for review.",
            _ => signalPhrase
        };
    }

    private static IReadOnlyList<FinancialAnalysisAiKeyFinding> BuildKeyFindings(
        IReadOnlyList<FinancialRiskSignal> riskSignals)
    {
        return riskSignals
            .Select(signal => new FinancialAnalysisAiKeyFinding(
                Title: string.IsNullOrWhiteSpace(signal.Name)
                    ? "Financial risk signal"
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
            ? "A financial risk signal was produced by the deterministic analysis."
            : $"A financial risk signal was produced for {signal.Period}.";
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
            ? "The analysis includes high-severity risk signals. Human review should focus on the underlying metrics and evidence."
            : riskSignals.Any(signal => IsSeverity(signal.Severity, "Medium"))
                ? "The analysis includes moderate risk indicators that should be reviewed in context."
                : riskSignals.Count == 0
                    ? "No material risk signals were produced by the deterministic financial analysis."
                    : "The analysis includes low-severity risk indicators that should be reviewed in context.";

        return warnings.Count > 0 || limitations.Count > 0
            ? $"{interpretation} The interpretation should be read together with the validation warnings and limitations."
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
                Message: "Fixture fallback metrics were used. This is intended for demo/development only.",
                Severity: "Warning",
                RelatedFields: ["metricsInputSource"]
            ));
        }

        if (GetMetricsSource(input.MetricsInputSource) == FinancialMetricsInputSources.None)
        {
            notes.Add(new FinancialAnalysisAiDataQualityNote(
                Message: "No structured financial metrics were available for review.",
                Severity: "Warning",
                RelatedFields: ["metricsInputSource"]
            ));
        }

        if (input.MetricsProvenance is { WarningCount: > 0 } provenance)
        {
            notes.Add(new FinancialAnalysisAiDataQualityNote(
                Message: $"The structured metrics ingestion produced {provenance.WarningCount} warning(s).",
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
