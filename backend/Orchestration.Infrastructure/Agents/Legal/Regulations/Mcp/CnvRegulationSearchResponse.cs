namespace Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

public sealed record CnvRegulationSearchResponse(
    string Query,
    IReadOnlyList<CnvRegulationSearchResult> Results,
    IReadOnlyList<string> Warnings
);

public sealed record CnvRegulationSearchResult(
    string DocumentId,
    string? ChunkId,
    string Title,
    string? Chapter,
    string? Section,
    string? Article,
    string Source,
    string? Url,
    string Snippet,
    double Score,
    IReadOnlyList<CnvRegulationCitation> Citations
);

public sealed record CnvRegulationCitation(
    string Source,
    string? DocumentType,
    string? ResolutionNumber,
    string Title,
    string? Chapter,
    string? Section,
    string? Article,
    string? PublicationDate,
    string? Url,
    string? QuotedText
);