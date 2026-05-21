namespace Orchestration.Application.Agents.Data;

public sealed class DataAgentOptions
{
    public const string SectionName = "DataAgent";

    public bool FinancialAnalysisToolsEnabled { get; init; }

    public bool UsePythonFinancialAnalysis { get; init; } = true;

    public bool UseLegacyAnomalyDetectionFallback { get; init; } = true;

    public bool UseFixtureMetricsFallback { get; init; }

    public bool RequireSessionFinancialMetrics { get; init; }

    public string RiskThresholdProfile { get; init; } =
        "default_oil_and_gas_equity_research";

    public string StructuredMetricsFixturePath { get; init; } = "";
}
