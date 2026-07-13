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
    private const string FinancialMetricsInputLimitation =
        "El análisis financiero puede procesar archivos PDF y ejecutar OCR de ser necesario, pero se recomiendan cargas estructuradas en JSON o CSV para preservar la fidelidad de los datos. No toma decisiones operativas.";
    private const string FixtureFallbackWarning =
        "Se utilizaron métricas de prueba predefinidas (fixtures). Este modo está destinado únicamente a desarrollo y demostración.";
    private const string RequiredMetricsWarning =
        "Se requieren métricas financieras estructuradas para este modo, pero no se adjuntaron a la sesión.";

    private const string IncompleteAnalysisLimitation =
        "El análisis financiero está incompleto y requiere revisión humana.";

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
                        "Se requerían métricas financieras estructuradas, pero no se adjuntaron a esta sesión.",
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
                RequestedRatios: RequestedRatios,
                SessionId: report.SessionId
            ),
            cancellationToken
        );

        var comparisons = await _financialAnalysisService.ComparePeriodsAsync(
            new ComparePeriodsRequest(
                Metrics: metricsDocument.Metrics,
                FromPeriod: BasePeriod,
                ToPeriod: ComparisonPeriod,
                MetricNames: MetricsToCompare,
                SessionId: report.SessionId
            ),
            cancellationToken
        );

        var signals = await _financialAnalysisService.DetectFinancialRiskSignalsAsync(
            new DetectFinancialRiskSignalsRequest(
                Metrics: metricsDocument.Metrics,
                Ratios: ratios.Ratios,
                Comparisons: comparisons.Comparisons,
                ThresholdProfileName: resolvedProfileName,
                Thresholds: resolvedThresholds,
                SessionId: report.SessionId
            ),
            cancellationToken
        );

        var summary = await _financialAnalysisService.SummarizeQuantitativeEvidenceAsync(
            new SummarizeQuantitativeEvidenceRequest(
                Metrics: metricsDocument.Metrics,
                Ratios: ratios.Ratios,
                Comparisons: comparisons.Comparisons,
                Signals: signals.Signals,
                MaxItems: 8,
                SessionId: report.SessionId
            ),
            cancellationToken
        );

        var execution = FinancialAnalysisExecution.FromStages(
        [
            ratios.Execution,
            comparisons.Execution,
            signals.Execution,
            summary.Execution
        ]);
        var signalsSucceeded =
            signals.Execution.Status == FinancialAnalysisExecutionStatus.Succeeded;
        var requiresHumanReview =
            execution.OverallStatus != FinancialAnalysisExecutionStatus.Succeeded;

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
                    "El análisis financiero utilizó métricas predefinidas de prueba (fixture) porque no se adjuntaron métricas a la sesión.",
                    DateTimeOffset.UtcNow
                ),
                cancellationToken
            );
        }

        var evidence = MapEvidence(
            signals.Signals,
            summary.Result.Evidence
        );
        var severity = signalsSucceeded
            ? ResolveSeverity(signals.Signals.Select(signal => signal.Severity))
            : "Unknown";
        var hasAnomaly = signalsSucceeded &&
            signals.Signals.Any(signal => IsMediumOrHigh(signal.Severity));
        var limitations = requiresHumanReview
            ? new[] { FinancialMetricsInputLimitation, IncompleteAnalysisLimitation }
            : new[] { FinancialMetricsInputLimitation };

        if (requiresHumanReview)
        {
            await PublishIncompleteExecutionActivityAsync(
                report.SessionId,
                execution,
                cancellationToken
            );
        }

        var aiReview = signalsSucceeded
            ? await _aiReviewService.ReviewAsync(
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
            )
            : FinancialAnalysisAiReviewResults.NotRun(
                "financial_analysis_execution_failed"
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
            {
                Execution = execution
            },
            RequiresHumanReview: requiresHumanReview
        );
    }

    private Task PublishIncompleteExecutionActivityAsync(
        Guid sessionId,
        FinancialAnalysisExecution execution,
        CancellationToken cancellationToken)
    {
        var eventType = execution.OverallStatus == FinancialAnalysisExecutionStatus.Failed
            ? "financial_analysis_execution_failed"
            : "financial_analysis_execution_degraded";
        var failedStages = execution.Stages
            .Where(stage => stage.Status != FinancialAnalysisExecutionStatus.Succeeded)
            .Select(stage =>
                $"{stage.Operation}:{GetSafeFailureCode(stage.FailureCode)}")
            .ToArray();

        return _activityPublisher.PublishAsync(
            new ActivityEvent(
                sessionId,
                eventType,
                "DataAgent",
                $"Análisis financiero incompleto. Etapas fallidas: {string.Join(", ", failedStages)}.",
                DateTimeOffset.UtcNow
            ),
            cancellationToken
        );
    }

    private static string GetSafeFailureCode(string? failureCode)
    {
        return failureCode switch
        {
            FinancialAnalysisFailureCodes.PythonInvocationFailed =>
                FinancialAnalysisFailureCodes.PythonInvocationFailed,
            FinancialAnalysisFailureCodes.PythonResponseInvalid =>
                FinancialAnalysisFailureCodes.PythonResponseInvalid,
            FinancialAnalysisFailureCodes.UnexpectedFailure =>
                FinancialAnalysisFailureCodes.UnexpectedFailure,
            _ => FinancialAnalysisFailureCodes.UnexpectedFailure
        };
    }

    private static DataAgentResult NoMetricsResult(
        FinancialReportContext report)
    {
        return new DataAgentResult(
            HasAnomaly: true,
            Severity: "Medium",
            Summary: "Las métricas financieras estructuradas no estaban disponibles. Se recomienda una revisión humana.",
            Engine: Engine,
            Evidence:
            [
                new AnomalyEvidence(
                    Metric: "StructuredFinancialMetrics",
                    Value: 0,
                    Threshold: 0,
                    Interpretation: "No había métricas financieras estructuradas disponibles para el análisis cuantitativo."
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
                Warnings: ["Las métricas financieras estructuradas no estaban disponibles."],
                Limitations: [FinancialMetricsInputLimitation],
                MetricsInputSource: FinancialMetricsInputSources.None,
                AiReview: FinancialAnalysisAiReviewResults.NotRun("structured_financial_metrics_missing")
            ),
            RequiresHumanReview: true
        );
    }

    private static DataAgentResult RequiredMetricsMissingResult(
        FinancialReportContext report)
    {
        return new DataAgentResult(
            HasAnomaly: true,
            Severity: "Medium",
            Summary: "Se requieren métricas financieras estructuradas, pero no se adjuntaron a esta sesión.",
            Engine: Engine,
            Evidence:
            [
                new AnomalyEvidence(
                    Metric: "StructuredFinancialMetricsRequired",
                    Value: 0,
                    Threshold: 1,
                    Interpretation: "Se requieren métricas financieras estructuradas para este modo, pero no se adjuntaron a la sesión."
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
                    "No se calcularon índices financieros ni comparaciones de períodos porque no había métricas estructuradas disponibles.",
                    FinancialMetricsInputLimitation
                ],
                MetricsInputSource: FinancialMetricsInputSources.None,
                AiReview: FinancialAnalysisAiReviewResults.NotRun("structured_financial_metrics_missing")
            ),
            RequiresHumanReview: true
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
            ? "Se detectaron señales de riesgo financiero. Se recomienda revisión humana."
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
