namespace Orchestration.Application.AnalysisSessions;

public sealed record AnalysisSessionDto(
    Guid Id,
    string Status,
    string? CurrentAgent,
    string ContextJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
