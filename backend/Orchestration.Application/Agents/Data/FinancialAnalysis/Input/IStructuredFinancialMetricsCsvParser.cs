namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public interface IStructuredFinancialMetricsCsvParser
{
    StructuredFinancialMetricsCsvParseResult Parse(
        StructuredFinancialMetricsCsvInput input);
}
