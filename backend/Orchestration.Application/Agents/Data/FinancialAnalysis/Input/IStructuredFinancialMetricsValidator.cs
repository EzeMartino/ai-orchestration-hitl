namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public interface IStructuredFinancialMetricsValidator
{
    FinancialMetricsValidationResult Validate(
        StructuredFinancialMetricsInput input);
}
