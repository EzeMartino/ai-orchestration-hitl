namespace Orchestration.Application.AnalysisSessions;

public sealed record AnalysisSessionStartPreflightIssue(
    string Code,
    string Message,
    string Severity
);
