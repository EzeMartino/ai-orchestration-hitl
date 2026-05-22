using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestration.Application.Activity;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;
using Orchestration.Application.Agents.Shared;
using Orchestration.Application.FinancialAnalysis.Thresholds;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis;

public sealed class DataAgentFinancialAnalysisWorkflow : IDataAgentFinancialAnalysisWorkflow
{
    private const string Engine = "Semantic Kernel + CSnakes + Python/Pandas";
    private const string BasePeriod = "2024A";
    private const string ComparisonPeriod = "2025E";
    private const string StructuredMetricsOnlyLimitation =
        "Financial analysis uses structured metrics only. It does not parse PDFs, perform OCR, or make operational decisions.";
    private const string FixtureFallbackWarning =
        "Fixture fallback metrics were used. This mode is intended for development/demo only.";
    private const string RequiredMetricsWarning =
        "Structured financial metrics are required for this mode but were not attached to the session.";

    private static readonly string[] RequestedRatios =
    [
        "gross_margin",
        "ebitda_margin",
        "net_margin",
        "current_ratio",
        "quick_ratio",
        "debt_to_equity",
        "net_debt_to_ebitda",
        "interest_coverage",
        "fcf_margin",
        "capex_to_revenue"
    ];

    private static readonly string[] MetricsToCompare =
    [
        "revenue",
        "gross_profit",
        "ebitda",
        "ebit",
        "net_income",
        "free_cash_flow",
        "total_debt",
        "net_debt",
        "capex"
    ];

    private readonly IStructuredFinancialMetricsProvider _metricsProvider;
    private readonly IPythonFinancialAnalysisService _financialAnalysisService;
    private readonly IDataAgentAiReviewService _aiReviewService;
    private readonly IFinancialRiskThresholdProfileProvider _profileProvider;
    private readonly DataAgentOptions _options;
    private readonly IActivityEventPublisher _activityPublisher;
    private readonly ILogger<DataAgentFinancialAnalysisWorkflow> _logger;

    public DataAgentFinancialAnalysisWorkflow(
        IStructuredFinancialMetricsProvider metricsProvider,
        IPythonFinancialAnalysisService financialAnalysisService,
        IDataAgentAiReviewService aiReviewService,
        IFinancialRiskThresholdProfileProvider profileProvider,
        IOptions<DataAgentOptions> options,
        IActivityEventPublisher activityPublisher,
        ILogger<DataAgentFinancialAnalysisWorkflow> logger)
    {
        _metricsProvider = metricsProvider;
        _financialAnalysisService = financialAnalysisService;
        _aiReviewService = aiReviewService;
        _profileProvider = profileProvider;
        _options = options.Value;
        _activityPublisher = activityPublisher;
        _logger = logger;
    }

    public async Task<DataAgentResult> AnalyzeAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        var resolution = _profileProvider.ResolveProfile(_options.RiskThresholdProfile);
        var resolvedProfileName = resolution.Profile.Name;
        var resolvedThresholds = resolution.Profile.Thresholds;

        _logger.LogDebug(
            "Running financial analysis workflow for {ReportName} using resolved threshold profile {RiskThresholdProfile}.",
            report.ReportName,
            resolvedProfileName
        );

        var metricsDocument = await _metricsProvider.GetMetricsAsync(
            report,
            cancellationToken
        );

        if (metricsDocument is null || metricsDocument.Metrics.Count == 0)
        {
            if (_options.RequireSessionFinancialMetrics)
            {
                await _activityPublisher.PublishAsync(
                    new ActivityEvent(
                        report.SessionId,
                        "financial_metrics_required_missing",
                        "DataAgent",
                        "Structured financial metrics were required but not attached to this session.",
                        DateTimeOffset.UtcNow
                    ),
                    cancellationToken
                );

                return RequiredMetricsMissingResult(report);
            }

            return NoMetricsResult(report);
        }

        var ratios = await _financialAnalysisService.ComputeFinancialRatiosAsync(
            new ComputeFinancialRatiosRequest(
                Metrics: metricsDocument.Metrics,
                RequestedRatios: RequestedRatios
            ),
            cancellationToken
        );

        var comparisons = await _financialAnalysisService.ComparePeriodsAsync(
            new ComparePeriodsRequest(
                Metrics: metricsDocument.Metrics,
                FromPeriod: BasePeriod,
                ToPeriod: ComparisonPeriod,
                MetricNames: MetricsToCompare
            ),
            cancellationToken
        );

        var signals = await _financialAnalysisService.DetectFinancialRiskSignalsAsync(
            new DetectFinancialRiskSignalsRequest(
                Metrics: metricsDocument.Metrics,
                Ratios: ratios.Ratios,
                Comparisons: comparisons.Comparisons,
                ThresholdProfileName: resolvedProfileName,
                Thresholds: resolvedThresholds
            ),
            cancellationToken
        );

        var summary = await _financialAnalysisService.SummarizeQuantitativeEvidenceAsync(
            new SummarizeQuantitativeEvidenceRequest(
                Metrics: metricsDocument.Metrics,
                Ratios: ratios.Ratios,
                Comparisons: comparisons.Comparisons,
                Signals: signals.Signals,
                MaxItems: 8
            ),
            cancellationToken
        );

        var warnings = ratios.Warnings
            .Concat(comparisons.Warnings)
            .Concat(signals.Result.Warnings)
            .Concat(summary.Result.Warnings)
            .Concat(GetInputSourceWarnings(metricsDocument))
            .Concat(resolution.Warnings)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (IsFixtureFallback(metricsDocument))
        {
            await _activityPublisher.PublishAsync(
                new ActivityEvent(
                    report.SessionId,
                    "financial_metrics_fixture_fallback_used",
                    "DataAgent",
                    "Financial analysis used fixture fallback metrics because no session metrics were attached.",
                    DateTimeOffset.UtcNow
                ),
                cancellationToken
            );
        }

        var evidence = MapEvidence(
            signals.Signals,
            summary.Result.Evidence
        );
        var severity = ResolveSeverity(signals.Signals.Select(signal => signal.Severity));
        var hasAnomaly = signals.Signals.Any(signal => IsMediumOrHigh(signal.Severity));
        var limitations = new[] { StructuredMetricsOnlyLimitation };
        var aiReview = await _aiReviewService.ReviewAsync(
            new FinancialAnalysisAiReviewInput(
                SessionId: report.SessionId.ToString(),
                DocumentId: metricsDocument.DocumentId,
                Company: metricsDocument.Company,
                MetricsInputSource: metricsDocument.InputSource,
                MetricsProvenance: metricsDocument.Provenance,
                Ratios: ratios.Ratios,
                PeriodComparisons: comparisons.Comparisons,
                RiskSignals: signals.Signals,
                RiskEvidence: summary.Result.Evidence,
                Warnings: warnings,
                Limitations: limitations,
                ThresholdProfile: resolvedProfileName,
                ThresholdsUsed: resolvedThresholds
            ),
            cancellationToken
        );

        return new DataAgentResult(
            HasAnomaly: hasAnomaly,
            Severity: severity,
            Summary: BuildSummary(summary.Narrative, warnings),
            Engine: Engine,
            Evidence: evidence,
            FinancialAnalysis: new FinancialAnalysisContext(
                Engine: Engine,
                DocumentId: metricsDocument.DocumentId,
                Company: metricsDocument.Company,
                Ratios: ratios.Ratios,
                Comparisons: comparisons.Comparisons,
                RiskSignals: signals.Signals,
                RiskEvidence: summary.Result.Evidence,
                Warnings: warnings,
                Limitations: limitations,
                MetricsInputSource: metricsDocument.InputSource,
                MetricsProvenance: metricsDocument.Provenance,
                AiReview: aiReview,
                ThresholdProfile: resolvedProfileName,
                ThresholdsUsed: resolvedThresholds
            )
        );
    }

    private static DataAgentResult NoMetricsResult(
        FinancialReportContext report)
    {
        return new DataAgentResult(
            HasAnomaly: true,
            Severity: "Medium",
            Summary: "Structured financial metrics were not available. Human review recommended.",
            Engine: Engine,
            Evidence:
            [
                new AnomalyEvidence(
                    Metric: "StructuredFinancialMetrics",
                    Value: 0,
                    Threshold: 0,
                    Interpretation: "No structured financial metrics were available for quantitative analysis."
                )
            ],
            FinancialAnalysis: new FinancialAnalysisContext(
                Engine: Engine,
                DocumentId: report.ReportName,
                Company: null,
                Ratios: [],
                Comparisons: [],
                RiskSignals: [],
                RiskEvidence: [],
                Warnings: ["Structured financial metrics were not available."],
                Limitations: [StructuredMetricsOnlyLimitation],
                MetricsInputSource: FinancialMetricsInputSources.None,
                AiReview: FinancialAnalysisAiReviewResults.NotRun("structured_financial_metrics_missing")
            )
        );
    }

    private static DataAgentResult RequiredMetricsMissingResult(
        FinancialReportContext report)
    {
        return new DataAgentResult(
            HasAnomaly: true,
            Severity: "Medium",
            Summary: "Structured financial metrics are required but were not attached to this session.",
            Engine: Engine,
            Evidence:
            [
                new AnomalyEvidence(
                    Metric: "StructuredFinancialMetricsRequired",
                    Value: 0,
                    Threshold: 1,
                    Interpretation: "Structured financial metrics are required for this mode but were not attached to the session."
                )
            ],
            FinancialAnalysis: new FinancialAnalysisContext(
                Engine: Engine,
                DocumentId: report.ReportName,
                Company: null,
                Ratios: [],
                Comparisons: [],
                RiskSignals: [],
                RiskEvidence: [],
                Warnings: [RequiredMetricsWarning],
                Limitations:
                [
                    "No financial ratios or period comparisons were computed because no structured metrics were available.",
                    StructuredMetricsOnlyLimitation
                ],
                MetricsInputSource: FinancialMetricsInputSources.None,
                AiReview: FinancialAnalysisAiReviewResults.NotRun("structured_financial_metrics_missing")
            )
        );
    }

    private static IReadOnlyList<string> GetInputSourceWarnings(
        StructuredFinancialMetricsDocument metricsDocument)
    {
        return IsFixtureFallback(metricsDocument)
            ? [FixtureFallbackWarning]
            : [];
    }

    private static bool IsFixtureFallback(
        StructuredFinancialMetricsDocument metricsDocument)
    {
        return string.Equals(
            metricsDocument.InputSource,
            FinancialMetricsInputSources.FixtureFallback,
            StringComparison.Ordinal
        );
    }

    private static IReadOnlyList<AnomalyEvidence> MapEvidence(
        IReadOnlyList<FinancialRiskSignal> signals,
        IReadOnlyList<RiskEvidenceItem> summaryEvidence)
    {
        return signals
            .SelectMany(signal => signal.Evidence.Select(item => MapEvidenceItem(item, signal)))
            .Concat(summaryEvidence.Select(item => MapEvidenceItem(item, null)))
            .DistinctBy(item => new
            {
                item.Metric,
                item.Value,
                item.Threshold,
                item.Interpretation
            })
            .ToList();
    }

    private static AnomalyEvidence MapEvidenceItem(
        RiskEvidenceItem item,
        FinancialRiskSignal? signal)
    {
        var interpretation = signal is null
            ? item.Interpretation
            : $"{signal.Name} ({signal.Severity}): {item.Interpretation}";

        return new AnomalyEvidence(
            Metric: item.MetricName,
            Value: Convert.ToDouble(item.Value),
            Threshold: Convert.ToDouble(item.Threshold ?? 0m),
            Interpretation: interpretation
        );
    }

    private static string BuildSummary(
        string narrative,
        IReadOnlyList<string> warnings)
    {
        var summary = string.IsNullOrWhiteSpace(narrative)
            ? "Financial risk signals detected. Human review recommended."
            : narrative;

        return warnings.Count == 0
            ? summary
            : $"{summary} Notes: {string.Join(" ", warnings)}";
    }

    private static string ResolveSeverity(
        IEnumerable<string> severities)
    {
        var values = severities.ToArray();

        if (values.Any(severity => IsSeverity(severity, "High")))
        {
            return "High";
        }

        if (values.Any(severity => IsSeverity(severity, "Medium")))
        {
            return "Medium";
        }

        if (values.Any(severity => IsSeverity(severity, "Low")))
        {
            return "Low";
        }

        return "Low";
    }

    private static bool IsMediumOrHigh(string severity)
    {
        return IsSeverity(severity, "Medium") ||
            IsSeverity(severity, "High");
    }

    private static bool IsSeverity(
        string severity,
        string expected)
    {
        return string.Equals(
            severity,
            expected,
            StringComparison.OrdinalIgnoreCase
        );
    }
}
