using System.Collections.Generic;

namespace Orchestration.Application.Agents.Legal.Regulations;

public sealed record RegulatoryEvidenceCitation(
    string Source,
    string? DocumentType,
    string? ResolutionNumber,
    string Title,
    string? Chapter,
    string? Section,
    string? Article,
    string? PublicationDate,
    string? Url,
    string? QuotedText);

public sealed record RegulatoryOriginalEvidence(
    string Snippet,
    RegulatoryEvidenceCitation Citation);

public sealed record RegulatoryCanonicalDocument(
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
    string Text,
    int OriginalTextLength,
    bool IsTruncated,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<RegulatoryEvidenceCitation> Citations);

public sealed record RegulatoryCanonicalArticle(
    RegulatoryEvidenceCitation Citation,
    string Text,
    double Confidence,
    int OriginalTextLength,
    bool IsTruncated);

public sealed record RegulatoryEvidenceEnrichment(
    string EnrichmentId,
    string DocumentId,
    string? ChunkId,
    int Rank,
    double Score,
    RegulatoryOriginalEvidence Original,
    RegulatoryCanonicalDocument? Document,
    RegulatoryCanonicalArticle? Article,
    string Status,
    IReadOnlyList<string> Limitations);

public static class RegulatoryEvidenceEnrichmentStatuses
{
    public const string Verified = "Verified";
    public const string Partial = "Partial";
    public const string Conflict = "Conflict";
    public const string Unavailable = "Unavailable";
}
