using System.Text.Json.Serialization;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record ComparePeriodsRequest(
    IReadOnlyList<FinancialMetric> Metrics,
    string FromPeriod,
    string ToPeriod,
    IReadOnlyList<string> MetricNames,
    [property: JsonIgnore] Guid? SessionId = null
);
