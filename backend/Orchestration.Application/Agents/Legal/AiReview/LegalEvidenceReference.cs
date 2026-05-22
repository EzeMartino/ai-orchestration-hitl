namespace Orchestration.Application.Agents.Legal.AiReview;

public sealed record LegalEvidenceReference(
    string Source,
    string Title,
    string? Url,
    string? Citation,
    string? Snippet,
    string? RegulationArea,
    double? Score
);
