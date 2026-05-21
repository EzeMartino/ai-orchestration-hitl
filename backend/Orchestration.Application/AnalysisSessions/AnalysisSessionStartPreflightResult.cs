namespace Orchestration.Application.AnalysisSessions;

public sealed record AnalysisSessionStartPreflightResult(
    bool CanStart,
    IReadOnlyList<AnalysisSessionStartPreflightIssue> Errors,
    IReadOnlyList<AnalysisSessionStartPreflightIssue> Warnings
)
{
    public static AnalysisSessionStartPreflightResult Allowed { get; } =
        new(CanStart: true, Errors: [], Warnings: []);
}
