namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public interface IStructuredFinancialMetricsTextParser
{
    StructuredFinancialMetricsPdfExtractionResult Parse(
        StructuredFinancialMetricsTextParseRequest request);
}
