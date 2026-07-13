namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record ComputeFinancialRatiosResponse(
    string Engine,
    IReadOnlyList<FinancialRatio> Ratios,
    IReadOnlyList<string> Warnings
)
{
    public FinancialAnalysisStageExecution Execution { get; init; } =
        FinancialAnalysisStageExecution.LegacyUnknown(FinancialAnalysisOperations.Ratios);
}
