using System.Text.Json.Serialization;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record ComputeFinancialRatiosRequest(
    IReadOnlyList<FinancialMetric> Metrics,
    IReadOnlyList<string> RequestedRatios,
    [property: JsonIgnore] Guid? SessionId = null
);
