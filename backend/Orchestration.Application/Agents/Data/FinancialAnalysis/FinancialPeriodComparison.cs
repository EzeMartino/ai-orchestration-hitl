namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record FinancialPeriodComparison(
    string MetricName,
    string FromPeriod,
    string ToPeriod,
    decimal FromValue,
    decimal ToValue,
    decimal AbsoluteChange,
    decimal? PercentageChange,
    string Unit,
    string Interpretation
);
