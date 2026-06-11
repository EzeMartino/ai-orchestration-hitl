namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public interface IFinancialMetricsExtractionCompletenessEvaluator
{
    FinancialMetricsExtractionDecision Evaluate(
        StructuredFinancialMetricsPdfExtractionResult result,
        FinancialMetricsExtractionOptions options);
}
