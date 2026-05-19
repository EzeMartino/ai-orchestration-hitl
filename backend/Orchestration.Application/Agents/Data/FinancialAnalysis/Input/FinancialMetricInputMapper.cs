namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed class FinancialMetricInputMapper : IFinancialMetricInputMapper
{
    private const string StructuredInputStatement = "structured_input";

    public IReadOnlyList<FinancialMetric> MapToFinancialMetrics(
        FinancialMetricsValidationResult validationResult)
    {
        if (validationResult is null || !validationResult.IsValid)
        {
            return [];
        }

        return validationResult.Metrics
            .Select(metric => new FinancialMetric(
                Name: metric.Name,
                Period: metric.Period,
                Value: metric.Value,
                Unit: metric.Unit,
                Statement: StructuredInputStatement,
                Source: metric.Source,
                Currency: metric.Currency,
                SourcePage: metric.SourcePage,
                Confidence: metric.Confidence
            ))
            .ToArray();
    }
}
