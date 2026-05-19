namespace Orchestration.Application.Agents.Planner.ToolCalling;

public sealed class ToolCallingOptions
{
    public const string SectionName = "ToolCalling";

    public bool Enabled { get; init; }

    public ToolCallingExecutionMode ExecutionMode { get; init; } =
        ToolCallingExecutionMode.Shadow;

    public bool FinancialAnalysisToolsEnabled { get; init; }

    public int MaxToolCalls { get; init; } = 3;

    public string[] AllowedTools { get; init; } =
    [
        "data.analyze_transactions",
        "legal.search_cnv_regulation"
    ];
}
