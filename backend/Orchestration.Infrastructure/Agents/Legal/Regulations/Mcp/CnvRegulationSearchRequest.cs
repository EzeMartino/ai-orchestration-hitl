namespace Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

public sealed record CnvRegulationSearchRequest(
    string Query,
    string? Area = null,
    int Limit = 5,
    string? Source = null,
    string? DocumentType = null,
    string? ResolutionNumber = null,
    string? Status = null,
    bool? RequiresReview = null
);