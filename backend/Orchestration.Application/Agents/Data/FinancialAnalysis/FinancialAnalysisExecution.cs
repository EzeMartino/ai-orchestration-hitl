namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record FinancialAnalysisExecution(
    FinancialAnalysisExecutionStatus OverallStatus,
    IReadOnlyList<FinancialAnalysisStageExecution> Stages)
{
    public static FinancialAnalysisExecution LegacyUnknown { get; } = new(
        FinancialAnalysisExecutionStatus.LegacyUnknown,
        []
    );

    public static FinancialAnalysisExecution FromStages(
        IEnumerable<FinancialAnalysisStageExecution> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);

        var stagesByOperation = new Dictionary<string, FinancialAnalysisStageExecution>(
            StringComparer.Ordinal
        );

        foreach (var stage in stages)
        {
            if (FinancialAnalysisOperations.All.Contains(
                stage.Operation,
                StringComparer.Ordinal))
            {
                stagesByOperation[stage.Operation] = stage;
            }
        }

        var normalizedStages = FinancialAnalysisOperations.All
            .Select(operation => stagesByOperation.GetValueOrDefault(operation)
                ?? FinancialAnalysisStageExecution.LegacyUnknown(operation))
            .ToArray();
        var overallStatus = normalizedStages.Single(stage =>
                stage.Operation == FinancialAnalysisOperations.Signals)
            .Status == FinancialAnalysisExecutionStatus.Failed
            ? FinancialAnalysisExecutionStatus.Failed
            : normalizedStages.All(stage =>
                stage.Status == FinancialAnalysisExecutionStatus.Succeeded)
                ? FinancialAnalysisExecutionStatus.Succeeded
                : FinancialAnalysisExecutionStatus.Degraded;

        return new FinancialAnalysisExecution(overallStatus, normalizedStages);
    }
}
