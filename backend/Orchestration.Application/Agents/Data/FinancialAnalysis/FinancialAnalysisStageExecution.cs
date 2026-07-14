namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record FinancialAnalysisStageExecution(
    string Operation,
    FinancialAnalysisExecutionStatus Status,
    long DurationMilliseconds,
    string? FailureCode = null)
{
    public static FinancialAnalysisStageExecution LegacyUnknown(string operation)
    {
        return new FinancialAnalysisStageExecution(
            operation,
            FinancialAnalysisExecutionStatus.LegacyUnknown,
            0
        );
    }
}
