using System.Collections.ObjectModel;
using System.Text.Json;
using Orchestration.Application.Agents.Legal.Cnv;
using Orchestration.Application.Agents.Legal.Regulations;
using static Orchestration.Application.Agents.Legal.Regulations.RegulatoryEvidenceIdentityNormalizer;

namespace Orchestration.Infrastructure.Agents.Planner.ToolCalling;

internal static class RegulatoryEvidenceEnrichmentMapper
{
    private const double MinimumScore = 0.40;
    private const int MaximumEnrichments = 2;
    private const int MaximumDocumentCharacters = 12_000;
    private const int MaximumArticleCharacters = 6_000;
    private const int MaximumDocumentCitations = 16;
    private const int MaximumMetadataEntries = 32;
    private const int MaximumMetadataKeyCharacters = 128;
    private const int MaximumMetadataValueCharacters = 512;
    private const int MaximumIdentityCharacters = 256;
    private const int MaximumTitleCharacters = 512;
    private const int MaximumUrlCharacters = 2_048;
    private const string AggregateIdentityConflictCode =
        "aggregate_identity_conflict";
    private const string AggregateCandidateKeyConflictCode =
        "aggregate_candidate_key_conflict";
    private const string AggregateIdentityConflictLimitation =
        "Se detectaron identidades regulatorias contradictorias al combinar verificaciones CNV; se requiere revisión humana.";
    private const string AggregateCandidateKeyConflictLimitation =
        "Se detectaron claves de candidato contradictorias al combinar verificaciones CNV; se requiere revisión humana.";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> EnrichmentStatuses = new(
        [
            RegulatoryEvidenceEnrichmentStatuses.Verified,
            RegulatoryEvidenceEnrichmentStatuses.Partial,
            RegulatoryEvidenceEnrichmentStatuses.Conflict,
            RegulatoryEvidenceEnrichmentStatuses.Unavailable
        ],
        StringComparer.Ordinal);
    private static readonly HashSet<string> StageStatuses = new(
        [
            LegalCnvEnrichmentStageStatuses.NotApplicable,
            LegalCnvEnrichmentStageStatuses.NotAttempted,
            LegalCnvEnrichmentStageStatuses.Succeeded,
            LegalCnvEnrichmentStageStatuses.Missing,
            LegalCnvEnrichmentStageStatuses.TimedOut,
            LegalCnvEnrichmentStageStatuses.Malformed,
            LegalCnvEnrichmentStageStatuses.Failed,
            LegalCnvEnrichmentStageStatuses.Conflict
        ],
        StringComparer.Ordinal);

    public static bool TrySnapshotEnrichments(
        IReadOnlyList<RegulatoryEvidenceEnrichment?>? source,
        out IReadOnlyList<RegulatoryEvidenceEnrichment>? snapshot)
    {
        snapshot = null;
        if (source is null)
        {
            return true;
        }

        if (source.Count > MaximumEnrichments)
        {
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var ranks = new HashSet<int>();
        var items = new List<RegulatoryEvidenceEnrichment>(source.Count);
        foreach (var item in source)
        {
            if (!TrySnapshotEnrichment(item, out var itemSnapshot) ||
                !ids.Add(itemSnapshot!.EnrichmentId) ||
                !ranks.Add(itemSnapshot.Rank))
            {
                return false;
            }

            items.Add(itemSnapshot);
        }

        snapshot = ReadOnly(items);
        return true;
    }

    public static bool TrySnapshotAudits(
        IReadOnlyList<LegalCnvEnrichmentAudit?>? source,
        int queryCount,
        out IReadOnlyList<LegalCnvEnrichmentAudit>? snapshot)
    {
        snapshot = null;
        if (source is null)
        {
            return true;
        }

        if (source.Count > MaximumEnrichments)
        {
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var ranks = new HashSet<int>();
        var items = new List<LegalCnvEnrichmentAudit>(source.Count);
        foreach (var item in source)
        {
            if (!TrySnapshotAudit(item, queryCount, out var itemSnapshot) ||
                !ids.Add(itemSnapshot!.EnrichmentId) ||
                !ranks.Add(itemSnapshot.Rank))
            {
                return false;
            }

            items.Add(itemSnapshot);
        }

        snapshot = ReadOnly(items);
        return true;
    }

    public static bool AreAligned(
        IReadOnlyList<RegulatoryEvidenceEnrichment>? enrichments,
        LegalQueryStrategyAudit? queryStrategy)
    {
        var audits = queryStrategy?.Enrichments;
        if (enrichments is null)
        {
            return audits is null or { Count: 0 };
        }

        if (queryStrategy is null || audits is null ||
            enrichments.Count != audits.Count)
        {
            return false;
        }

        var auditsById = audits.ToDictionary(
            audit => audit.EnrichmentId,
            StringComparer.Ordinal);
        foreach (var enrichment in enrichments)
        {
            if (!auditsById.TryGetValue(enrichment.EnrichmentId, out var audit) ||
                enrichment.Rank != audit.Rank ||
                enrichment.Score != audit.Score ||
                enrichment.Status != audit.Status ||
                !StageMatchesSnapshot(
                    audit.Document,
                    enrichment.Document?.OriginalTextLength,
                    enrichment.Document?.IsTruncated,
                    enrichment.Document is not null) ||
                !StageMatchesSnapshot(
                    audit.Article,
                    enrichment.Article?.OriginalTextLength,
                    enrichment.Article?.IsTruncated,
                    enrichment.Article is not null))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StageMatchesSnapshot(
        LegalCnvEnrichmentStageAudit stage,
        int? originalTextLength,
        bool? isTruncated,
        bool hasSnapshot)
    {
        var stageCarriesSnapshot = stage.Status is
            LegalCnvEnrichmentStageStatuses.Succeeded or
            LegalCnvEnrichmentStageStatuses.Conflict;
        return stageCarriesSnapshot == hasSnapshot &&
            (!hasSnapshot ||
             stage.OriginalTextLength == originalTextLength &&
             stage.IsTruncated == isTruncated);
    }

    public static IReadOnlyList<RegulatoryEvidenceEnrichment>? MergeEnrichments(
        IReadOnlyList<Orchestration.Application.Agents.Legal.LegalAgentResult> results)
    {
        var all = results
            .SelectMany(result => result.EvidenceEnrichments ?? [])
            .ToArray();
        if (all.Length == 0)
        {
            return results.All(result => result.EvidenceEnrichments is null)
                ? null
                : Array.Empty<RegulatoryEvidenceEnrichment>();
        }

        var candidateKeyConflicts = results
            .SelectMany(result =>
                (result.QueryStrategy as LegalQueryStrategyAudit)?.Enrichments ?? [])
            .GroupBy(audit => audit.EnrichmentId, StringComparer.Ordinal)
            .Where(group => group
                .Select(audit => audit.CandidateKey)
                .Distinct(StringComparer.Ordinal)
                .Skip(1)
                .Any())
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        return ReadOnly(all
            .GroupBy(item => item.EnrichmentId, StringComparer.Ordinal)
            .Select(group => MergeEnrichmentGroup(
                group,
                candidateKeyConflicts.Contains(group.Key)))
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.EnrichmentId, StringComparer.Ordinal));
    }

    public static IReadOnlyList<LegalCnvEnrichmentAudit>? MergeAudits(
        IReadOnlyList<(LegalQueryStrategyAudit Strategy, int QueryOffset)> strategies,
        IReadOnlyList<RegulatoryEvidenceEnrichment>? mergedEnrichments,
        IReadOnlyList<Orchestration.Application.Agents.Legal.LegalAgentResult> results)
    {
        var all = strategies
            .SelectMany(item => item.Strategy.Enrichments?
                .Select(audit => new OffsetAudit(audit, item.QueryOffset)) ?? [])
            .ToArray();
        if (all.Length == 0)
        {
            return strategies.All(item => item.Strategy.Enrichments is null)
                ? null
                : Array.Empty<LegalCnvEnrichmentAudit>();
        }

        var mergedById = (mergedEnrichments ?? [])
            .ToDictionary(item => item.EnrichmentId, StringComparer.Ordinal);
        var sourceById = results
            .SelectMany(result => result.EvidenceEnrichments ?? [])
            .GroupBy(item => item.EnrichmentId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.Ordinal);
        return ReadOnly(all
            .GroupBy(item => item.Audit.EnrichmentId, StringComparer.Ordinal)
            .Select(group => MergeAuditGroup(
                group,
                mergedById,
                sourceById))
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.EnrichmentId, StringComparer.Ordinal));
    }

    private static bool TrySnapshotEnrichment(
        RegulatoryEvidenceEnrichment? item,
        out RegulatoryEvidenceEnrichment? snapshot)
    {
        snapshot = null;
        if (item is null ||
            string.IsNullOrWhiteSpace(item.EnrichmentId) ||
            string.IsNullOrWhiteSpace(item.DocumentId) ||
            item.Rank is < 1 or > MaximumEnrichments ||
            !double.IsFinite(item.Score) ||
            item.Score < MinimumScore ||
            !EnrichmentStatuses.Contains(item.Status) ||
            !TrySnapshotOriginal(item.Original, out var original) ||
            !TrySnapshotDocument(item.Document, out var document) ||
            !TrySnapshotArticle(item.Article, out var article) ||
            !TrySnapshotStrings(item.Limitations, requireNonBlank: true, out var limitations) ||
            !HasValidContentForStatus(item.Status, document, article) ||
            !HasConsistentIdentities(
                item.DocumentId,
                original!,
                document,
                article,
                item.Status))
        {
            return false;
        }

        snapshot = new RegulatoryEvidenceEnrichment(
            item.EnrichmentId,
            item.DocumentId,
            item.ChunkId,
            item.Rank,
            item.Score,
            original!,
            document,
            article,
            item.Status,
            limitations!);
        return true;
    }

    private static bool TrySnapshotOriginal(
        RegulatoryOriginalEvidence? original,
        out RegulatoryOriginalEvidence? snapshot)
    {
        snapshot = null;
        if (original is null ||
            original.Snippet is null ||
            !TrySnapshotCitation(original.Citation, canonical: false, out var citation))
        {
            return false;
        }

        snapshot = new RegulatoryOriginalEvidence(original.Snippet, citation!);
        return true;
    }

    private static bool TrySnapshotDocument(
        RegulatoryCanonicalDocument? document,
        out RegulatoryCanonicalDocument? snapshot)
    {
        snapshot = null;
        if (document is null)
        {
            return true;
        }

        if (!IsRequiredBounded(document.Id, MaximumIdentityCharacters) ||
            !IsRequiredBounded(document.Source, MaximumIdentityCharacters) ||
            !IsRequiredBounded(document.DocumentType, MaximumIdentityCharacters) ||
            !IsOptionalBounded(document.ResolutionNumber, MaximumIdentityCharacters) ||
            !IsRequiredBounded(document.Title, MaximumTitleCharacters) ||
            !IsOptionalBounded(document.PublicationDate, MaximumIdentityCharacters) ||
            !IsOptionalBounded(document.EffectiveDate, MaximumIdentityCharacters) ||
            !IsRequiredBounded(document.Url, MaximumUrlCharacters) ||
            !IsRequiredBounded(document.Status, MaximumIdentityCharacters) ||
            !IsOptionalBounded(document.RetrievedAt, MaximumIdentityCharacters) ||
            !IsValidBoundedText(
                document.Text,
                document.OriginalTextLength,
                document.IsTruncated,
                MaximumDocumentCharacters) ||
            document.Metadata is null ||
            document.Metadata.Count > MaximumMetadataEntries ||
            document.Metadata.Any(pair =>
                pair.Key is null ||
                pair.Value is null ||
                pair.Key.Length > MaximumMetadataKeyCharacters ||
                pair.Value.Length > MaximumMetadataValueCharacters) ||
            document.Citations is null ||
            document.Citations.Count > MaximumDocumentCitations)
        {
            return false;
        }

        var citations = new List<RegulatoryEvidenceCitation>(
            document.Citations.Count);
        foreach (var citation in document.Citations)
        {
            if (!TrySnapshotCitation(citation, canonical: true, out var citationSnapshot))
            {
                return false;
            }

            citations.Add(citationSnapshot!);
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in document.Metadata.OrderBy(
                     pair => pair.Key,
                     StringComparer.Ordinal))
        {
            if (!metadata.TryAdd(pair.Key, pair.Value))
            {
                return false;
            }
        }

        snapshot = new RegulatoryCanonicalDocument(
            document.Id,
            document.Source,
            document.DocumentType,
            document.ResolutionNumber,
            document.Title,
            document.PublicationDate,
            document.EffectiveDate,
            document.Url,
            document.Status,
            document.RequiresReview,
            document.RetrievedAt,
            document.Text,
            document.OriginalTextLength,
            document.IsTruncated,
            new ReadOnlyDictionary<string, string>(metadata),
            ReadOnly(citations));
        return true;
    }

    private static bool TrySnapshotArticle(
        RegulatoryCanonicalArticle? article,
        out RegulatoryCanonicalArticle? snapshot)
    {
        snapshot = null;
        if (article is null)
        {
            return true;
        }

        if (!TrySnapshotCitation(article.Citation, canonical: true, out var citation) ||
            string.IsNullOrWhiteSpace(citation?.Article) ||
            !IsValidBoundedText(
                article.Text,
                article.OriginalTextLength,
                article.IsTruncated,
                MaximumArticleCharacters) ||
            !double.IsFinite(article.Confidence))
        {
            return false;
        }

        snapshot = new RegulatoryCanonicalArticle(
            citation!,
            article.Text,
            article.Confidence,
            article.OriginalTextLength,
            article.IsTruncated);
        return true;
    }

    private static bool TrySnapshotCitation(
        RegulatoryEvidenceCitation? citation,
        bool canonical,
        out RegulatoryEvidenceCitation? snapshot)
    {
        snapshot = null;
        if (citation is null ||
            string.IsNullOrWhiteSpace(citation.Source) ||
            string.IsNullOrWhiteSpace(citation.Title) ||
            canonical &&
            (!IsRequiredBounded(citation.Source, MaximumIdentityCharacters) ||
             !IsOptionalBounded(citation.DocumentType, MaximumIdentityCharacters) ||
             !IsOptionalBounded(citation.ResolutionNumber, MaximumIdentityCharacters) ||
             !IsRequiredBounded(citation.Title, MaximumTitleCharacters) ||
             !IsOptionalBounded(citation.Chapter, MaximumIdentityCharacters) ||
             !IsOptionalBounded(citation.Section, MaximumIdentityCharacters) ||
             !IsOptionalBounded(citation.Article, MaximumIdentityCharacters) ||
             !IsOptionalBounded(citation.PublicationDate, MaximumIdentityCharacters) ||
             !IsOptionalBounded(citation.Url, MaximumUrlCharacters) ||
             !IsOptionalBounded(citation.QuotedText, MaximumArticleCharacters)))
        {
            return false;
        }

        snapshot = citation with { };
        return true;
    }

    private static bool TrySnapshotAudit(
        LegalCnvEnrichmentAudit? audit,
        int queryCount,
        out LegalCnvEnrichmentAudit? snapshot)
    {
        snapshot = null;
        if (audit is null ||
            string.IsNullOrWhiteSpace(audit.EnrichmentId) ||
            audit.Rank is < 1 or > MaximumEnrichments ||
            string.IsNullOrWhiteSpace(audit.CandidateKey) ||
            !double.IsFinite(audit.Score) ||
            audit.Score < MinimumScore ||
            audit.ContributingQueryIndices is null ||
            audit.ContributingQueryIndices.Count == 0 ||
            audit.ContributingQueryIndices.Any(index =>
                index < 1 || index > queryCount) ||
            audit.ContributingQueryIndices.Distinct().Count() !=
                audit.ContributingQueryIndices.Count ||
            !audit.ContributingQueryIndices.SequenceEqual(
                audit.ContributingQueryIndices.Order()) ||
            !TrySnapshotStage(audit.Document, out var document) ||
            !TrySnapshotStage(audit.Article, out var article) ||
            !EnrichmentStatuses.Contains(audit.Status) ||
            !TrySnapshotStrings(
                audit.LimitationCodes,
                requireNonBlank: true,
                out var limitationCodes))
        {
            return false;
        }

        var immutableLimitationCodes = limitationCodes!;
        var determinedStatus = DetermineStatus(document!, article!);
        var hasAggregateConflictCode = immutableLimitationCodes.Contains(
                AggregateIdentityConflictCode,
                StringComparer.Ordinal) ||
            immutableLimitationCodes.Contains(
                AggregateCandidateKeyConflictCode,
                StringComparer.Ordinal);
        var isSnapshotlessAggregateConflict =
            audit.Status == RegulatoryEvidenceEnrichmentStatuses.Conflict &&
            determinedStatus != RegulatoryEvidenceEnrichmentStatuses.Conflict &&
            hasAggregateConflictCode;
        if ((audit.Status != determinedStatus &&
                !isSnapshotlessAggregateConflict) ||
            (audit.Status != RegulatoryEvidenceEnrichmentStatuses.Conflict &&
                hasAggregateConflictCode))
        {
            return false;
        }

        snapshot = new LegalCnvEnrichmentAudit(
            audit.EnrichmentId,
            audit.Rank,
            audit.CandidateKey,
            audit.Score,
            ReadOnly(audit.ContributingQueryIndices),
            document!,
            article!,
            audit.Status,
            immutableLimitationCodes);
        return true;
    }

    private static bool TrySnapshotStage(
        LegalCnvEnrichmentStageAudit? stage,
        out LegalCnvEnrichmentStageAudit? snapshot)
    {
        snapshot = null;
        if (stage is null ||
            !StageStatuses.Contains(stage.Status) ||
            stage.OriginalTextLength is < 0 ||
            stage.Attempted && stage.FromCache ||
            !stage.Selected &&
                (stage.Attempted ||
                 stage.FromCache ||
                 stage.Status is not
                     (LegalCnvEnrichmentStageStatuses.NotApplicable or
                      LegalCnvEnrichmentStageStatuses.NotAttempted)) ||
            stage.Selected &&
                stage.Status == LegalCnvEnrichmentStageStatuses.NotApplicable ||
            stage.Status == LegalCnvEnrichmentStageStatuses.NotAttempted &&
                (stage.Attempted || stage.FromCache) ||
            stage.Selected &&
                stage.Status is not
                    (LegalCnvEnrichmentStageStatuses.NotApplicable or
                     LegalCnvEnrichmentStageStatuses.NotAttempted) &&
                !stage.Attempted &&
                !stage.FromCache ||
            stage.Status is
                (LegalCnvEnrichmentStageStatuses.Succeeded or
                 LegalCnvEnrichmentStageStatuses.Conflict) &&
                (!stage.Selected ||
                 !stage.Attempted && !stage.FromCache ||
                 stage.OriginalTextLength is null) ||
            stage.Status is not
                (LegalCnvEnrichmentStageStatuses.Succeeded or
                 LegalCnvEnrichmentStageStatuses.Conflict) &&
                (stage.OriginalTextLength is not null || stage.IsTruncated) ||
            stage.IsTruncated && stage.OriginalTextLength is not > 0)
        {
            return false;
        }

        snapshot = stage with { };
        return true;
    }

    private static bool TrySnapshotStrings(
        IReadOnlyList<string>? source,
        bool requireNonBlank,
        out IReadOnlyList<string>? snapshot)
    {
        snapshot = null;
        if (source is null ||
            source.Any(value =>
                value is null ||
                requireNonBlank && string.IsNullOrWhiteSpace(value)))
        {
            return false;
        }

        snapshot = ReadOnly(source);
        return true;
    }

    private static bool HasValidContentForStatus(
        string status,
        RegulatoryCanonicalDocument? document,
        RegulatoryCanonicalArticle? article) =>
        status switch
        {
            RegulatoryEvidenceEnrichmentStatuses.Verified =>
                document is not null || article is not null,
            RegulatoryEvidenceEnrichmentStatuses.Partial =>
                document is not null || article is not null,
            RegulatoryEvidenceEnrichmentStatuses.Unavailable =>
                document is null && article is null,
            RegulatoryEvidenceEnrichmentStatuses.Conflict => true,
            _ => false
        };

    private static bool HasConsistentIdentities(
        string documentId,
        RegulatoryOriginalEvidence original,
        RegulatoryCanonicalDocument? document,
        RegulatoryCanonicalArticle? article,
        string status)
    {
        if (status == RegulatoryEvidenceEnrichmentStatuses.Conflict)
        {
            return true;
        }

        if (document is not null &&
            NormalizeText(document.Id) != NormalizeText(documentId))
        {
            return false;
        }

        var originalCitation = original.Citation;
        if (document is not null &&
            (RequiredIdentityConflict(
                 originalCitation.Source,
                 document.Source) ||
             OptionalIdentityConflict(
                 originalCitation.ResolutionNumber,
                 document.ResolutionNumber)))
        {
            return false;
        }

        if (article is null)
        {
            return true;
        }

        return !RequiredIdentityConflict(
                originalCitation.Source,
                article.Citation.Source) &&
            !OptionalIdentityConflict(
                originalCitation.ResolutionNumber,
                article.Citation.ResolutionNumber) &&
            !OptionalLocatorConflict(
                originalCitation.Article,
                article.Citation.Article);
    }

    private static string DetermineStatus(
        LegalCnvEnrichmentStageAudit document,
        LegalCnvEnrichmentStageAudit article)
    {
        if (document.Status == LegalCnvEnrichmentStageStatuses.Conflict ||
            article.Status == LegalCnvEnrichmentStageStatuses.Conflict)
        {
            return RegulatoryEvidenceEnrichmentStatuses.Conflict;
        }

        var selected = new[] { document, article }
            .Where(stage => stage.Selected)
            .ToArray();
        if (!selected.Any(stage =>
                stage.Status == LegalCnvEnrichmentStageStatuses.Succeeded))
        {
            return RegulatoryEvidenceEnrichmentStatuses.Unavailable;
        }

        return selected.All(stage =>
            stage.Status == LegalCnvEnrichmentStageStatuses.Succeeded)
            ? RegulatoryEvidenceEnrichmentStatuses.Verified
            : RegulatoryEvidenceEnrichmentStatuses.Partial;
    }

    private static bool IsValidBoundedText(
        string? text,
        int originalLength,
        bool isTruncated,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            text.Length > maximumLength ||
            originalLength < text.Length)
        {
            return false;
        }

        return isTruncated
            ? originalLength > text.Length
            : originalLength == text.Length;
    }

    private static bool IsRequiredBounded(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;

    private static bool IsOptionalBounded(string? value, int maximum) =>
        value is null || value.Length <= maximum;

    private static RegulatoryEvidenceEnrichment MergeEnrichmentGroup(
        IGrouping<string, RegulatoryEvidenceEnrichment> group,
        bool candidateKeyConflict)
    {
        var items = group.ToArray();
        var identityConflict = items
            .Select(CreateStableIdentity)
            .Distinct(StringComparer.Ordinal)
            .Skip(1)
            .Any() ||
            HasCanonicalIdentityConflict(items);
        var explicitConflict = items.Any(item =>
            item.Status == RegulatoryEvidenceEnrichmentStatuses.Conflict);
        var representativeCandidates = explicitConflict
            ? items.Where(item =>
                item.Status == RegulatoryEvidenceEnrichmentStatuses.Conflict)
            : items.AsEnumerable();
        var representative = representativeCandidates
            .OrderByDescending(item => explicitConflict
                ? CanonicalSnapshotRichness(item)
                : EnrichmentRichness(item))
            .ThenByDescending(CanonicalSnapshotRichness)
            .ThenByDescending(item =>
                (item.Document?.Text.Length ?? 0) +
                (item.Article?.Text.Length ?? 0))
            .ThenBy(Serialize, StringComparer.Ordinal)
            .First();
        var limitations = items
            .SelectMany(item => item.Limitations)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (identityConflict)
        {
            limitations.Add(AggregateIdentityConflictLimitation);
        }

        if (candidateKeyConflict)
        {
            limitations.Add(AggregateCandidateKeyConflictLimitation);
        }

        return new RegulatoryEvidenceEnrichment(
            group.Key,
            representative.DocumentId,
            representative.ChunkId,
            items.Min(item => item.Rank),
            items.Max(item => item.Score),
            representative.Original,
            representative.Document,
            representative.Article,
            identityConflict || candidateKeyConflict || explicitConflict
                ? RegulatoryEvidenceEnrichmentStatuses.Conflict
                : representative.Status,
            ReadOnly(limitations
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)));
    }

    private static LegalCnvEnrichmentAudit MergeAuditGroup(
        IGrouping<string, OffsetAudit> group,
        IReadOnlyDictionary<string, RegulatoryEvidenceEnrichment> mergedById,
        IReadOnlyDictionary<string, RegulatoryEvidenceEnrichment[]> sourceById)
    {
        var items = group.ToArray();
        var audits = items.Select(item => item.Audit).ToArray();
        var enrichment = mergedById[group.Key];
        var sourceEnrichments = sourceById[group.Key];
        var identityConflict = sourceEnrichments
            .Select(CreateStableIdentity)
            .Distinct(StringComparer.Ordinal)
            .Skip(1)
            .Any() ||
            HasCanonicalIdentityConflict(sourceEnrichments);
        var candidateKeyConflict = audits
            .Select(audit => audit.CandidateKey)
            .Distinct(StringComparer.Ordinal)
            .Skip(1)
            .Any();
        var representative = audits
            .Where(audit =>
                AuditMatchesEnrichmentSnapshots(audit, enrichment))
            .OrderByDescending(audit =>
                audit.Status ==
                    RegulatoryEvidenceEnrichmentStatuses.Conflict)
            .ThenByDescending(audit => StageRichness(audit.Document) +
                StageRichness(audit.Article))
            .ThenBy(Serialize, StringComparer.Ordinal)
            .FirstOrDefault() ??
            audits
                .OrderByDescending(audit =>
                    audit.Status ==
                        RegulatoryEvidenceEnrichmentStatuses.Conflict)
                .ThenByDescending(audit => StageRichness(audit.Document) +
                    StageRichness(audit.Article))
                .ThenBy(Serialize, StringComparer.Ordinal)
                .First();
        var document = representative.Document;
        var article = representative.Article;
        if (enrichment.Status == RegulatoryEvidenceEnrichmentStatuses.Conflict &&
            document.Status != LegalCnvEnrichmentStageStatuses.Conflict &&
            article.Status != LegalCnvEnrichmentStageStatuses.Conflict)
        {
            if (enrichment.Document is not null)
            {
                document = document with
                {
                    Status = LegalCnvEnrichmentStageStatuses.Conflict
                };
            }
            else if (enrichment.Article is not null)
            {
                article = article with
                {
                    Status = LegalCnvEnrichmentStageStatuses.Conflict
                };
            }
        }
        var limitationCodes = audits
            .SelectMany(audit => audit.LimitationCodes)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (identityConflict)
        {
            limitationCodes.Add(AggregateIdentityConflictCode);
        }

        if (candidateKeyConflict)
        {
            limitationCodes.Add(AggregateCandidateKeyConflictCode);
        }

        return new LegalCnvEnrichmentAudit(
            group.Key,
            enrichment.Rank,
            audits.Select(audit => audit.CandidateKey)
                .OrderBy(value => value, StringComparer.Ordinal)
                .First(),
            audits.Max(audit => audit.Score),
            ReadOnly(items
                .SelectMany(item => item.Audit.ContributingQueryIndices
                    .Select(index => index + item.QueryOffset))
                .Distinct()
                .Order()),
            document,
            article,
            enrichment.Status,
            ReadOnly(limitationCodes
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)));
    }

    private static int EnrichmentRichness(RegulatoryEvidenceEnrichment item) =>
        item.Status switch
        {
            RegulatoryEvidenceEnrichmentStatuses.Verified => 3,
            RegulatoryEvidenceEnrichmentStatuses.Partial => 2,
            RegulatoryEvidenceEnrichmentStatuses.Unavailable => 1,
            _ => 0
        };

    private static int CanonicalSnapshotRichness(
        RegulatoryEvidenceEnrichment item) =>
        (item.Document is null ? 0 : 2) +
        (item.Article is null ? 0 : 1);

    private static int StageRichness(LegalCnvEnrichmentStageAudit stage) =>
        stage.Status switch
        {
            LegalCnvEnrichmentStageStatuses.Conflict => 8,
            LegalCnvEnrichmentStageStatuses.Succeeded => 7,
            LegalCnvEnrichmentStageStatuses.Malformed => 6,
            LegalCnvEnrichmentStageStatuses.TimedOut => 5,
            LegalCnvEnrichmentStageStatuses.Failed => 4,
            LegalCnvEnrichmentStageStatuses.Missing => 3,
            LegalCnvEnrichmentStageStatuses.NotAttempted => 2,
            _ => 1
        };

    private static bool AuditMatchesEnrichmentSnapshots(
        LegalCnvEnrichmentAudit audit,
        RegulatoryEvidenceEnrichment enrichment) =>
        AuditStageMatchesSnapshot(
            audit.Document,
            enrichment.Document?.OriginalTextLength,
            enrichment.Document?.IsTruncated,
            enrichment.Document is not null) &&
        AuditStageMatchesSnapshot(
            audit.Article,
            enrichment.Article?.OriginalTextLength,
            enrichment.Article?.IsTruncated,
            enrichment.Article is not null);

    private static bool AuditStageMatchesSnapshot(
        LegalCnvEnrichmentStageAudit stage,
        int? originalTextLength,
        bool? isTruncated,
        bool hasSnapshot)
    {
        var carriesSnapshot = stage.Status is
            LegalCnvEnrichmentStageStatuses.Succeeded or
            LegalCnvEnrichmentStageStatuses.Conflict;
        return carriesSnapshot == hasSnapshot &&
            (!hasSnapshot ||
             stage.OriginalTextLength == originalTextLength &&
             stage.IsTruncated == isTruncated);
    }

    private static bool HasCanonicalIdentityConflict(
        IReadOnlyList<RegulatoryEvidenceEnrichment> items)
    {
        var documents = items
            .Where(item => item.Document is not null)
            .Select(item => item.Document!)
            .ToArray();
        for (var left = 0; left < documents.Length; left++)
        {
            for (var right = left + 1; right < documents.Length; right++)
            {
                if (RequiredIdentityConflict(
                        documents[left].Id,
                        documents[right].Id) ||
                    RequiredIdentityConflict(
                        documents[left].Source,
                        documents[right].Source) ||
                    OptionalIdentityConflict(
                        documents[left].ResolutionNumber,
                        documents[right].ResolutionNumber))
                {
                    return true;
                }
            }
        }

        var articles = items
            .Where(item => item.Article is not null)
            .Select(item => item.Article!)
            .ToArray();
        for (var left = 0; left < articles.Length; left++)
        {
            for (var right = left + 1; right < articles.Length; right++)
            {
                if (RequiredLocatorConflict(
                        articles[left].Citation.Article,
                        articles[right].Citation.Article) ||
                    RequiredIdentityConflict(
                        articles[left].Citation.Source,
                        articles[right].Citation.Source) ||
                    OptionalIdentityConflict(
                        articles[left].Citation.ResolutionNumber,
                        articles[right].Citation.ResolutionNumber))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool RequiredIdentityConflict(string? left, string? right) =>
        NormalizeText(left) != NormalizeText(right);

    private static bool OptionalIdentityConflict(string? left, string? right)
    {
        var normalizedLeft = NormalizeText(left);
        var normalizedRight = NormalizeText(right);
        return normalizedLeft.Length > 0 &&
            normalizedRight.Length > 0 &&
            normalizedLeft != normalizedRight;
    }

    private static bool RequiredLocatorConflict(string? left, string? right) =>
        NormalizeLocator(left) != NormalizeLocator(right);

    private static bool OptionalLocatorConflict(string? left, string? right)
    {
        var normalizedLeft = NormalizeLocator(left);
        var normalizedRight = NormalizeLocator(right);
        return normalizedLeft.Length > 0 &&
            normalizedRight.Length > 0 &&
            normalizedLeft != normalizedRight;
    }

    private static string CreateStableIdentity(
        RegulatoryEvidenceEnrichment item) =>
        string.Join(
            "\u001f",
            NormalizeText(item.DocumentId),
            NormalizeLocator(
                FirstNonBlank(
                    item.Original.Citation.Article,
                    item.Original.Citation.Section,
                    item.Original.Citation.Chapter)));

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> source) =>
        Array.AsReadOnly(source.ToArray());

    private sealed record OffsetAudit(
        LegalCnvEnrichmentAudit Audit,
        int QueryOffset);
}
