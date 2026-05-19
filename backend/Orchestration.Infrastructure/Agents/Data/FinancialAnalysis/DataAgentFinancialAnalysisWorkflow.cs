using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis;

public sealed class DataAgentFinancialAnalysisWorkflow : IDataAgentFinancialAnalysisWorkflow
{
    private const string Engine = "Semantic Kernel + CSnakes + Python/Pandas";
    private const string BasePeriod = "2024A";
    private const string ComparisonPeriod = "2025E";

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
    private readonly DataAgentOptions _options;
    private readonly ILogger<DataAgentFinancialAnalysisWorkflow> _logger;

    public DataAgentFinancialAnalysisWorkflow(
        IStructuredFinancialMetricsProvider metricsProvider,
        IPythonFinancialAnalysisService financialAnalysisService,
        IOptions<DataAgentOptions> options,
        ILogger<DataAgentFinancialAnalysisWorkflow> logger)
    {
        _metricsProvider = metricsProvider;
        _financialAnalysisService = financialAnalysisService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DataAgentResult> AnalyzeAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Running financial analysis workflow for {ReportName} using threshold profile {RiskThresholdProfile}.",
            report.ReportName,
            _options.RiskThresholdProfile
        );

        var metricsDocument = await _metricsProvider.GetMetricsAsync(
            report,
            cancellationToken
        );

        if (metricsDocument is null || metricsDocument.Metrics.Count == 0)
        {
            return NoMetricsResult();
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
                Comparisons: comparisons.Comparisons
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
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var evidence = MapEvidence(
            signals.Signals,
            summary.Result.Evidence,
            warnings
        );
        var severity = ResolveSeverity(signals.Signals.Select(signal => signal.Severity));
        var hasAnomaly = IsMediumOrHigh(severity) ||
            IsMediumOrHigh(signals.Result.RiskLevel) ||
            signals.Signals.Any(signal => IsMediumOrHigh(signal.Severity));

        return new DataAgentResult(
            HasAnomaly: hasAnomaly,
            Severity: severity,
            Summary: BuildSummary(summary.Narrative, warnings),
            Engine: Engine,
            Evidence: evidence
        );
    }

    private static DataAgentResult NoMetricsResult()
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
            ]
        );
    }

    private static IReadOnlyList<AnomalyEvidence> MapEvidence(
        IReadOnlyList<FinancialRiskSignal> signals,
        IReadOnlyList<RiskEvidenceItem> summaryEvidence,
        IReadOnlyList<string> warnings)
    {
        var evidence = signals
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

        if (warnings.Count > 0)
        {
            evidence.Add(new AnomalyEvidence(
                Metric: "FinancialAnalysisWarning",
                Value: 0,
                Threshold: 0,
                Interpretation: string.Join(" ", warnings)
            ));
        }

        return evidence;
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
