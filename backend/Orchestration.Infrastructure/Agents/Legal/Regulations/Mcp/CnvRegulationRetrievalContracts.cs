namespace Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

public sealed record CnvRegulationDocumentRequest(string DocumentId);

public sealed record CnvRegulationArticleRequest(
    string Article,
    string? Title = null,
    string? Chapter = null,
    string? Section = null);

public sealed record CnvRegulationDocumentResponse(
    bool Found,
    CnvRegulationDocument? Document,
    IReadOnlyList<CnvRegulationCitation>? Citations,
    IReadOnlyList<string>? Warnings);

public sealed record CnvRegulationArticleResponse(
    bool Found,
    string? Text,
    CnvRegulationCitation? Citation,
    double Confidence,
    IReadOnlyList<string>? Warnings);

public sealed record CnvRegulationDocument(
    string Id,
    string Source,
    string DocumentType,
    string? ResolutionNumber,
    string Title,
    string? PublicationDate,
    string? EffectiveDate,
    string Url,
    string Status,
    bool RequiresReview,
    string? RetrievedAt,
    IReadOnlyDictionary<string, string>? Metadata,
    string Text);
