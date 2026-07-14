namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record SummarizeQuantitativeEvidenceResponse(
    string Engine,
    string Narrative,
    FinancialAnalysisToolResult Result
)
{
    public FinancialAnalysisStageExecution Execution { get; init; } =
        FinancialAnalysisStageExecution.LegacyUnknown(FinancialAnalysisOperations.Summary);
}
