using System.Collections.ObjectModel;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Legal.Cnv;
using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;
using static Orchestration.Application.Agents.Legal.Regulations.RegulatoryEvidenceIdentityNormalizer;

namespace Orchestration.Infrastructure.Agents.Legal.Regulations;

internal sealed record CnvRegulationEnrichmentHit(
    int QueryIndex,
    CnvRegulationSearchResult Result);

internal sealed record CnvRegulatoryHitEnrichmentResult(
    IReadOnlyList<RegulatoryEvidenceEnrichment> Enrichments,
    IReadOnlyList<LegalCnvEnrichmentAudit> Audits,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Deterministically enriches a bounded set of CNV regulation search hits.
/// </summary>
/// <param name="client">Client used for canonical document and article retrieval.</param>
/// <param name="options">Bounds and selection options for the enrichment run.</param>
/// <param name="logger">Logger for identifier-only stage diagnostics.</param>
public sealed class CnvRegulatoryHitEnricher(
    ICnvRegulationMcpClient client,
    IOptions<CnvRegulationMcpOptions> options,
    ILogger<CnvRegulatoryHitEnricher> logger)
{
    private const double MinimumScore = 0.40;
    private const int AbsoluteMaximumHits = 2;
    private const int MaximumCanonicalDocumentCitations = 16;
    private const int MaximumCanonicalMetadataEntries = 32;
    private const int MaximumMetadataKeyCharacters = 128;
    private const int MaximumMetadataValueCharacters = 512;
    private const int MaximumCanonicalIdentityFieldCharacters = 256;
    private const int MaximumCanonicalTitleCharacters = 512;
    private const int MaximumCanonicalUrlCharacters = 2_048;
    private const string EmptySha256Hex =
        "0000000000000000000000000000000000000000000000000000000000000000";

    private readonly ICnvRegulationMcpClient _client =
        client ?? throw new ArgumentNullException(nameof(client));
    private readonly CnvRegulationMcpOptions _options =
        (options ?? throw new ArgumentNullException(nameof(options))).Value;
    private readonly ILogger<CnvRegulatoryHitEnricher> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    internal async Task<CnvRegulatoryHitEnrichmentResult> EnrichAsync(
        IReadOnlyList<CnvRegulationEnrichmentHit> hits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hits);
        cancellationToken.ThrowIfCancellationRequested();

        var maximumHits = Math.Clamp(_options.MaxEnrichedHits, 0, AbsoluteMaximumHits);
        if (maximumHits == 0)
        {
            return EmptyResult();
        }

        var candidates = SelectCandidates(hits, maximumHits);
        if (candidates.Count == 0)
        {
            return EmptyResult();
        }

        var enrichments = new List<RegulatoryEvidenceEnrichment>(candidates.Count);
        var audits = new List<LegalCnvEnrichmentAudit>(candidates.Count);
        var warningSet = new SortedSet<string>(StringComparer.Ordinal);
        var documentCache = new Dictionary<string, DocumentLookupOutcome>(StringComparer.Ordinal);

        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = candidates[index];
            var rank = index + 1;
            var enrichmentId = CreateDomainSeparatedHash("enrichment", candidate.Identity);
            var limitations = new List<string>();
            var limitationCodes = new List<string>();

            var document = await EnrichDocumentAsync(
                candidate,
                enrichmentId,
                rank,
                documentCache,
                limitations,
                limitationCodes,
                cancellationToken).ConfigureAwait(false);

            var article = await EnrichArticleAsync(
                candidate,
                enrichmentId,
                rank,
                document.Audit.Status == LegalCnvEnrichmentStageStatuses.Conflict,
                limitations,
                limitationCodes,
                cancellationToken).ConfigureAwait(false);

            var status = DetermineFinalStatus(document.Audit, article.Audit);
            var immutableLimitations = ReadOnlyOrdinalDistinct(limitations);
            var immutableLimitationCodes = ReadOnlyOrdinalDistinct(limitationCodes);
            enrichments.Add(new RegulatoryEvidenceEnrichment(
                EnrichmentId: enrichmentId,
                DocumentId: candidate.RequestDocumentId,
                ChunkId: candidate.Result.ChunkId,
                Rank: rank,
                Score: candidate.Result.Score,
                Original: new RegulatoryOriginalEvidence(
                    candidate.Result.Snippet,
                    MapOriginalCitation(candidate.PrimaryCitation)),
                Document: document.Document,
                Article: article.Article,
                Status: status,
                Limitations: immutableLimitations));

            audits.Add(new LegalCnvEnrichmentAudit(
                EnrichmentId: enrichmentId,
                Rank: rank,
                CandidateKey: candidate.AuditCandidateKey,
                Score: candidate.Result.Score,
                ContributingQueryIndices: candidate.ContributingQueryIndices,
                Document: document.Audit,
                Article: article.Audit,
                Status: status,
                LimitationCodes: immutableLimitationCodes));

            foreach (var limitation in immutableLimitations)
            {
                warningSet.Add(limitation);
            }
        }

        _logger.LogInformation(
            "CNV hit enrichment completed with {CandidateCount} candidates and {WarningCount} limitations.",
            enrichments.Count,
            warningSet.Count);

        return new CnvRegulatoryHitEnrichmentResult(
            ReadOnly(enrichments),
            ReadOnly(audits),
            ReadOnly(warningSet));
    }

    private async Task<DocumentEnrichmentOutcome> EnrichDocumentAsync(
        Candidate candidate,
        string enrichmentId,
        int rank,
        IDictionary<string, DocumentLookupOutcome> cache,
        ICollection<string> limitations,
        ICollection<string> limitationCodes,
        CancellationToken cancellationToken)
    {
        if (_options.MaxDocumentContextCharacters <= 0)
        {
            var disabledAudit = StageAudit(
                selected: false,
                attempted: false,
                fromCache: false,
                LegalCnvEnrichmentStageStatuses.NotAttempted);
            LogStage(enrichmentId, rank, "get_document", disabledAudit);
            return new DocumentEnrichmentOutcome(null, disabledAudit);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var documentCacheKey = CreateIdentityFingerprint(candidate.RequestDocumentId).Hash;
        DocumentLookupOutcome lookupOutcome;
        var fromCache = cache.TryGetValue(documentCacheKey, out var cachedOutcome);
        if (fromCache)
        {
            lookupOutcome = cachedOutcome!;
        }
        else
        {
            lookupOutcome = await RetrieveDocumentAsync(candidate.RequestDocumentId, cancellationToken)
                .ConfigureAwait(false);
            cache[documentCacheKey] = lookupOutcome;
        }

        AppendBoundaryLimitations(
            lookupOutcome.Limitations,
            lookupOutcome.LimitationCodes,
            limitations,
            limitationCodes);
        var status = lookupOutcome.Status;
        if (status == LegalCnvEnrichmentStageStatuses.Succeeded &&
            HasDocumentConflict(candidate, lookupOutcome))
        {
            status = LegalCnvEnrichmentStageStatuses.Conflict;
        }

        AddLimitation(
            "document",
            status,
            lookupOutcome.IsTruncated,
            limitations,
            limitationCodes);
        var audit = StageAudit(
            selected: true,
            attempted: !fromCache,
            fromCache,
            status,
            lookupOutcome.OriginalTextLength,
            lookupOutcome.IsTruncated);
        LogStage(enrichmentId, rank, "get_document", audit);
        return new DocumentEnrichmentOutcome(lookupOutcome.Snapshot, audit);
    }

    private async Task<ArticleEnrichmentOutcome> EnrichArticleAsync(
        Candidate candidate,
        string enrichmentId,
        int rank,
        bool documentConflict,
        ICollection<string> limitations,
        ICollection<string> limitationCodes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(candidate.ArticleLocator))
        {
            var notApplicable = StageAudit(
                selected: false,
                attempted: false,
                fromCache: false,
                LegalCnvEnrichmentStageStatuses.NotApplicable);
            LogStage(enrichmentId, rank, "get_article", notApplicable);
            return new ArticleEnrichmentOutcome(null, notApplicable);
        }

        if (_options.MaxArticleContextCharacters <= 0)
        {
            var disabledAudit = StageAudit(
                selected: false,
                attempted: false,
                fromCache: false,
                LegalCnvEnrichmentStageStatuses.NotAttempted);
            LogStage(enrichmentId, rank, "get_article", disabledAudit);
            return new ArticleEnrichmentOutcome(null, disabledAudit);
        }

        if (documentConflict)
        {
            var skippedAudit = StageAudit(
                selected: true,
                attempted: false,
                fromCache: false,
                LegalCnvEnrichmentStageStatuses.NotAttempted);
            LogStage(enrichmentId, rank, "get_article", skippedAudit);
            return new ArticleEnrichmentOutcome(null, skippedAudit);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var request = new CnvRegulationArticleRequest(
            Article: candidate.ArticleLocator!,
            Title: FirstNonBlank(candidate.PrimaryCitation.Title, candidate.Result.Title),
            Chapter: FirstNonBlank(candidate.PrimaryCitation.Chapter, candidate.Result.Chapter),
            Section: FirstNonBlank(candidate.PrimaryCitation.Section, candidate.Result.Section));
        var lookupOutcome = await RetrieveArticleAsync(request, cancellationToken).ConfigureAwait(false);
        AppendBoundaryLimitations(
            lookupOutcome.Limitations,
            lookupOutcome.LimitationCodes,
            limitations,
            limitationCodes);
        var status = lookupOutcome.Status;
        if (status == LegalCnvEnrichmentStageStatuses.Succeeded &&
            HasArticleConflict(candidate, lookupOutcome))
        {
            status = LegalCnvEnrichmentStageStatuses.Conflict;
        }

        AddLimitation(
            "article",
            status,
            lookupOutcome.IsTruncated,
            limitations,
            limitationCodes);
        var audit = StageAudit(
            selected: true,
            attempted: true,
            fromCache: false,
            status,
            lookupOutcome.OriginalTextLength,
            lookupOutcome.IsTruncated);
        LogStage(enrichmentId, rank, "get_article", audit);
        return new ArticleEnrichmentOutcome(lookupOutcome.Snapshot, audit);
    }

    private async Task<DocumentLookupOutcome> RetrieveDocumentAsync(
        string documentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetDocumentAsync(
                new CnvRegulationDocumentRequest(documentId),
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return CreateDocumentLookupOutcome(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return EmptyDocumentLookup(LegalCnvEnrichmentStageStatuses.TimedOut);
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return EmptyDocumentLookup(LegalCnvEnrichmentStageStatuses.Failed);
        }
    }

    private async Task<ArticleLookupOutcome> RetrieveArticleAsync(
        CnvRegulationArticleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetArticleAsync(request, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return CreateArticleLookupOutcome(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return EmptyArticleLookup(LegalCnvEnrichmentStageStatuses.TimedOut);
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return EmptyArticleLookup(LegalCnvEnrichmentStageStatuses.Failed);
        }
    }

    private DocumentLookupOutcome CreateDocumentLookupOutcome(CnvRegulationDocumentResponse? response)
    {
        if (response is null || response.Warnings is null || response.Citations is null)
        {
            return EmptyDocumentLookup(LegalCnvEnrichmentStageStatuses.Malformed);
        }

        if (!response.Found)
        {
            return response.Document is null
                ? EmptyDocumentLookup(LegalCnvEnrichmentStageStatuses.Missing)
                : EmptyDocumentLookup(LegalCnvEnrichmentStageStatuses.Malformed);
        }

        if (response.Document is null ||
            string.IsNullOrWhiteSpace(response.Document.Id) ||
            string.IsNullOrWhiteSpace(response.Document.Source) ||
            string.IsNullOrWhiteSpace(response.Document.DocumentType) ||
            string.IsNullOrWhiteSpace(response.Document.Title) ||
            string.IsNullOrWhiteSpace(response.Document.Url) ||
            string.IsNullOrWhiteSpace(response.Document.Status) ||
            string.IsNullOrWhiteSpace(response.Document.Text) ||
            response.Citations.Any(citation =>
                citation is null ||
                string.IsNullOrWhiteSpace(citation.Source) ||
                string.IsNullOrWhiteSpace(citation.Title)))
        {
            return EmptyDocumentLookup(LegalCnvEnrichmentStageStatuses.Malformed);
        }

        var snapshot = BuildDocumentSnapshot(response.Document, response.Citations);
        return new DocumentLookupOutcome(
            Status: LegalCnvEnrichmentStageStatuses.Succeeded,
            Snapshot: snapshot.Snapshot,
            IdFingerprint: CreateIdentityFingerprint(response.Document.Id),
            SourceFingerprint: CreateIdentityFingerprint(response.Document.Source),
            ResolutionFingerprint: CreateIdentityFingerprint(response.Document.ResolutionNumber),
            OriginalTextLength: snapshot.Snapshot.OriginalTextLength,
            IsTruncated: snapshot.Snapshot.IsTruncated,
            Limitations: snapshot.Limitations,
            LimitationCodes: snapshot.LimitationCodes);
    }

    private ArticleLookupOutcome CreateArticleLookupOutcome(CnvRegulationArticleResponse? response)
    {
        if (response is null || response.Warnings is null)
        {
            return EmptyArticleLookup(LegalCnvEnrichmentStageStatuses.Malformed);
        }

        if (!response.Found)
        {
            return response.Text is null && response.Citation is null
                ? EmptyArticleLookup(LegalCnvEnrichmentStageStatuses.Missing)
                : EmptyArticleLookup(LegalCnvEnrichmentStageStatuses.Malformed);
        }

        if (string.IsNullOrWhiteSpace(response.Text) ||
            response.Citation is null ||
            string.IsNullOrWhiteSpace(response.Citation.Article) ||
            string.IsNullOrWhiteSpace(response.Citation.Source) ||
            string.IsNullOrWhiteSpace(response.Citation.Title) ||
            !double.IsFinite(response.Confidence))
        {
            return EmptyArticleLookup(LegalCnvEnrichmentStageStatuses.Malformed);
        }

        var snapshot = BuildArticleSnapshot(response);
        return new ArticleLookupOutcome(
            Status: LegalCnvEnrichmentStageStatuses.Succeeded,
            Snapshot: snapshot.Snapshot,
            ArticleFingerprint: CreateLocatorFingerprint(response.Citation.Article),
            SourceFingerprint: CreateIdentityFingerprint(response.Citation.Source),
            ResolutionFingerprint: CreateIdentityFingerprint(response.Citation.ResolutionNumber),
            OriginalTextLength: snapshot.Snapshot.OriginalTextLength,
            IsTruncated: snapshot.Snapshot.IsTruncated,
            Limitations: snapshot.Limitations,
            LimitationCodes: snapshot.LimitationCodes);
    }

    private static IReadOnlyList<Candidate> SelectCandidates(
        IReadOnlyList<CnvRegulationEnrichmentHit> hits,
        int maximumHits)
    {
        var groups = new Dictionary<CandidateIdentity, CandidateGroup>();
        foreach (var hit in hits)
        {
            if (!TryCreateCandidate(hit, out var candidate))
            {
                continue;
            }

            if (!groups.TryGetValue(candidate.Identity, out var group))
            {
                groups[candidate.Identity] = new CandidateGroup(candidate, hit!.QueryIndex);
                continue;
            }

            group.ContributingQueryIndices.Add(hit!.QueryIndex);
            if (CompareRepresentatives(candidate, group.Representative) < 0)
            {
                group.Representative = candidate;
            }
        }

        return groups.Values
            .Select(group => group.Representative with
            {
                ContributingQueryIndices = ReadOnly(group.ContributingQueryIndices)
            })
            .OrderByDescending(candidate => candidate.Result.Score)
            .ThenBy(candidate => candidate.Identity.NormalizedDocumentId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Identity.NormalizedLocator, StringComparer.Ordinal)
            .ThenBy(candidate => NormalizeText(candidate.Result.ChunkId), StringComparer.Ordinal)
            .ThenBy(candidate => candidate.StableSnapshot, StringComparer.Ordinal)
            .Take(maximumHits)
            .ToArray();
    }

    private static bool TryCreateCandidate(
        CnvRegulationEnrichmentHit? hit,
        out Candidate candidate)
    {
        candidate = null!;
        var result = hit?.Result;
        if (result is null ||
            !double.IsFinite(result.Score) ||
            result.Score < MinimumScore ||
            string.IsNullOrWhiteSpace(result.DocumentId) ||
            result.Citations is not { Count: > 0 })
        {
            return false;
        }

        var primaryCitation = SelectPrimaryCitation(result.Citations);
        if (primaryCitation is null)
        {
            return false;
        }

        var normalizedDocumentId = NormalizeText(result.DocumentId);
        var primaryLocator = FirstNonBlank(
            primaryCitation.Article,
            primaryCitation.Section,
            primaryCitation.Chapter,
            result.Article,
            result.Section,
            result.Chapter) ?? string.Empty;
        var normalizedLocator = NormalizeLocator(primaryLocator);
        var identity = new CandidateIdentity(normalizedDocumentId, normalizedLocator);
        var requestDocumentId = result.DocumentId.Trim();
        var articleLocator = FirstNonBlank(primaryCitation.Article, result.Article);
        candidate = new Candidate(
            Result: result,
            PrimaryCitation: primaryCitation,
            RequestDocumentId: requestDocumentId,
            ArticleLocator: articleLocator,
            Identity: identity,
            AuditCandidateKey: CreateDomainSeparatedHash("candidate", identity),
            StableSnapshot: CreateStableSnapshot(result, primaryCitation),
            ContributingQueryIndices: ReadOnly([hit!.QueryIndex]));
        return true;
    }

    private static CnvRegulationCitation? SelectPrimaryCitation(
        IReadOnlyList<CnvRegulationCitation> citations) =>
        citations
            .Where(citation => citation is not null)
            .OrderBy(CitationPriority)
            .ThenBy(citation => NormalizeText(citation.Source), StringComparer.Ordinal)
            .ThenBy(citation => NormalizeText(citation.Title), StringComparer.Ordinal)
            .ThenBy(citation => NormalizeLocator(CitationLocator(citation)), StringComparer.Ordinal)
            .ThenBy(citation => NormalizeText(citation.Url), StringComparer.Ordinal)
            .ThenBy(CreateCitationSnapshot, StringComparer.Ordinal)
            .FirstOrDefault();

    private static int CitationPriority(CnvRegulationCitation citation)
    {
        if (!string.IsNullOrWhiteSpace(citation.Article)) return 0;
        if (!string.IsNullOrWhiteSpace(citation.Section)) return 1;
        if (!string.IsNullOrWhiteSpace(citation.Chapter)) return 2;
        return 3;
    }

    private static string? CitationLocator(CnvRegulationCitation citation) =>
        FirstNonBlank(citation.Article, citation.Section, citation.Chapter);

    private static int CompareRepresentatives(Candidate left, Candidate right)
    {
        var scoreComparison = right.Result.Score.CompareTo(left.Result.Score);
        if (scoreComparison != 0)
        {
            return scoreComparison;
        }

        var chunkComparison = StringComparer.Ordinal.Compare(
            NormalizeText(left.Result.ChunkId),
            NormalizeText(right.Result.ChunkId));
        return chunkComparison != 0
            ? chunkComparison
            : StringComparer.Ordinal.Compare(left.StableSnapshot, right.StableSnapshot);
    }

    private static string CreateStableSnapshot(
        CnvRegulationSearchResult result,
        CnvRegulationCitation primaryCitation)
    {
        var builder = new StringBuilder();
        AppendStable(builder, result.DocumentId);
        AppendStable(builder, result.ChunkId);
        AppendStable(builder, result.Title);
        AppendStable(builder, result.Chapter);
        AppendStable(builder, result.Section);
        AppendStable(builder, result.Article);
        AppendStable(builder, result.Source);
        AppendStable(builder, result.Url);
        AppendStable(builder, result.Snippet);
        builder.Append(result.Score.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        builder.Append(CreateCitationSnapshot(primaryCitation));
        return builder.ToString();
    }

    private static string CreateCitationSnapshot(CnvRegulationCitation citation)
    {
        var builder = new StringBuilder();
        AppendStable(builder, citation.Source);
        AppendStable(builder, citation.DocumentType);
        AppendStable(builder, citation.ResolutionNumber);
        AppendStable(builder, citation.Title);
        AppendStable(builder, citation.Chapter);
        AppendStable(builder, citation.Section);
        AppendStable(builder, citation.Article);
        AppendStable(builder, citation.PublicationDate);
        AppendStable(builder, citation.Url);
        AppendStable(builder, citation.QuotedText);
        return builder.ToString();
    }

    private static void AppendStable(StringBuilder builder, string? value)
    {
        value ??= string.Empty;
        var normalized = NormalizeText(value);
        builder
            .Append(normalized.Length)
            .Append(':')
            .Append(normalized)
            .Append('|')
            .Append(value.Length)
            .Append(':')
            .Append(value)
            .Append('|');
    }

    private static bool HasDocumentConflict(
        Candidate candidate,
        DocumentLookupOutcome outcome) =>
        !CreateIdentityFingerprint(candidate.RequestDocumentId).Equals(outcome.IdFingerprint) ||
        FingerprintsConflict(candidate.PrimaryCitation.Source, outcome.SourceFingerprint) ||
        FingerprintsConflict(
            candidate.PrimaryCitation.ResolutionNumber,
            outcome.ResolutionFingerprint);

    private static bool HasArticleConflict(
        Candidate candidate,
        ArticleLookupOutcome outcome) =>
        !CreateLocatorFingerprint(candidate.ArticleLocator).Equals(outcome.ArticleFingerprint) ||
        FingerprintsConflict(candidate.PrimaryCitation.Source, outcome.SourceFingerprint) ||
        FingerprintsConflict(
            candidate.PrimaryCitation.ResolutionNumber,
            outcome.ResolutionFingerprint);

    private static bool FingerprintsConflict(
        string? original,
        IdentityFingerprint canonical)
    {
        var originalFingerprint = CreateIdentityFingerprint(original);
        return originalFingerprint.HasValue &&
            canonical.HasValue &&
            !originalFingerprint.Equals(canonical);
    }

    private DocumentSnapshotBuildResult BuildDocumentSnapshot(
        CnvRegulationDocument document,
        IReadOnlyList<CnvRegulationCitation> citations)
    {
        var boundedText = Bound(document.Text, _options.MaxDocumentContextCharacters);
        var fieldsTruncated = false;
        var id = BoundCanonicalField(document.Id, MaximumCanonicalIdentityFieldCharacters);
        var source = BoundCanonicalField(document.Source, MaximumCanonicalIdentityFieldCharacters);
        var documentType = BoundCanonicalField(
            document.DocumentType,
            MaximumCanonicalIdentityFieldCharacters);
        var resolutionNumber = BoundCanonicalField(
            document.ResolutionNumber,
            MaximumCanonicalIdentityFieldCharacters);
        var title = BoundCanonicalField(document.Title, MaximumCanonicalTitleCharacters);
        var publicationDate = BoundCanonicalField(
            document.PublicationDate,
            MaximumCanonicalIdentityFieldCharacters);
        var effectiveDate = BoundCanonicalField(
            document.EffectiveDate,
            MaximumCanonicalIdentityFieldCharacters);
        var url = BoundCanonicalField(document.Url, MaximumCanonicalUrlCharacters);
        var status = BoundCanonicalField(document.Status, MaximumCanonicalIdentityFieldCharacters);
        var retrievedAt = BoundCanonicalField(
            document.RetrievedAt,
            MaximumCanonicalIdentityFieldCharacters);
        fieldsTruncated = id.IsTruncated || source.IsTruncated || documentType.IsTruncated ||
            resolutionNumber.IsTruncated || title.IsTruncated || publicationDate.IsTruncated ||
            effectiveDate.IsTruncated || url.IsTruncated || status.IsTruncated ||
            retrievedAt.IsTruncated;

        var metadata = BuildMetadataSnapshot(document.Metadata);
        var canonicalCitations = BuildDocumentCitationSnapshot(citations);
        var boundary = CreateBoundaryLimitations(
            fieldsTruncated,
            metadata.WasAltered,
            canonicalCitations.WasAltered);
        var snapshot = new RegulatoryCanonicalDocument(
            Id: id.Value!,
            Source: source.Value!,
            DocumentType: documentType.Value!,
            ResolutionNumber: resolutionNumber.Value,
            Title: title.Value!,
            PublicationDate: publicationDate.Value,
            EffectiveDate: effectiveDate.Value,
            Url: url.Value!,
            Status: status.Value!,
            RequiresReview: document.RequiresReview,
            RetrievedAt: retrievedAt.Value,
            Text: boundedText.Text,
            OriginalTextLength: boundedText.OriginalLength,
            IsTruncated: boundedText.IsTruncated,
            Metadata: metadata.Metadata,
            Citations: canonicalCitations.Citations);
        return new DocumentSnapshotBuildResult(
            snapshot,
            boundary.Limitations,
            boundary.LimitationCodes);
    }

    private ArticleSnapshotBuildResult BuildArticleSnapshot(CnvRegulationArticleResponse response)
    {
        var boundedText = Bound(response.Text!, _options.MaxArticleContextCharacters);
        var citation = SanitizeCanonicalCitation(
            response.Citation!,
            _options.MaxArticleContextCharacters,
            retainQuotedText: true);
        var boundary = CreateBoundaryLimitations(
            fieldsTruncated: citation.WasAltered,
            metadataTruncated: false,
            citationsTruncated: false);
        var snapshot = new RegulatoryCanonicalArticle(
            Citation: citation.Citation,
            Text: boundedText.Text,
            Confidence: response.Confidence,
            OriginalTextLength: boundedText.OriginalLength,
            IsTruncated: boundedText.IsTruncated);
        return new ArticleSnapshotBuildResult(
            snapshot,
            boundary.Limitations,
            boundary.LimitationCodes);
    }

    private static MetadataSnapshot BuildMetadataSnapshot(
        IReadOnlyDictionary<string, string>? source)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        var wasAltered = false;
        if (source is not null)
        {
            foreach (var pair in source.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (pair.Key is null)
                {
                    wasAltered = true;
                    continue;
                }

                var key = BoundCanonicalField(pair.Key, MaximumMetadataKeyCharacters);
                var value = BoundCanonicalField(pair.Value ?? string.Empty, MaximumMetadataValueCharacters);
                wasAltered |= key.IsTruncated || value.IsTruncated || pair.Value is null;
                if (metadata.ContainsKey(key.Value!))
                {
                    wasAltered = true;
                    continue;
                }

                if (metadata.Count >= MaximumCanonicalMetadataEntries)
                {
                    wasAltered = true;
                    continue;
                }

                metadata[key.Value!] = value.Value!;
            }
        }

        return new MetadataSnapshot(
            new ReadOnlyDictionary<string, string>(metadata),
            wasAltered);
    }

    private static CitationSnapshot BuildDocumentCitationSnapshot(
        IReadOnlyList<CnvRegulationCitation> source)
    {
        var candidates = source
            .Select(citation => SanitizeCanonicalCitation(
                citation,
                maximumQuotedTextCharacters: 0,
                retainQuotedText: false))
            .OrderBy(candidate => candidate.Identity.Source, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Identity.DocumentType, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Identity.ResolutionNumber, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Identity.Title, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Identity.Chapter, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Identity.Section, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Identity.Article, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Identity.PublicationDate, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Identity.Url, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Identity.QuotedText, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.TieBreaker, StringComparer.Ordinal)
            .ToArray();
        var seen = new HashSet<CanonicalCitationIdentity>();
        var retained = new List<RegulatoryEvidenceCitation>(MaximumCanonicalDocumentCitations);
        var wasAltered = candidates.Any(candidate => candidate.WasAltered);
        foreach (var candidate in candidates)
        {
            if (!seen.Add(candidate.Identity))
            {
                wasAltered = true;
                continue;
            }

            if (retained.Count >= MaximumCanonicalDocumentCitations)
            {
                wasAltered = true;
                continue;
            }

            retained.Add(candidate.Citation);
        }

        return new CitationSnapshot(ReadOnly(retained), wasAltered);
    }

    private static SanitizedCanonicalCitation SanitizeCanonicalCitation(
        CnvRegulationCitation citation,
        int maximumQuotedTextCharacters,
        bool retainQuotedText)
    {
        var source = BoundCanonicalField(citation.Source, MaximumCanonicalIdentityFieldCharacters);
        var documentType = BoundCanonicalField(
            citation.DocumentType,
            MaximumCanonicalIdentityFieldCharacters);
        var resolutionNumber = BoundCanonicalField(
            citation.ResolutionNumber,
            MaximumCanonicalIdentityFieldCharacters);
        var title = BoundCanonicalField(citation.Title, MaximumCanonicalTitleCharacters);
        var chapter = BoundCanonicalField(citation.Chapter, MaximumCanonicalIdentityFieldCharacters);
        var section = BoundCanonicalField(citation.Section, MaximumCanonicalIdentityFieldCharacters);
        var article = BoundCanonicalField(citation.Article, MaximumCanonicalIdentityFieldCharacters);
        var publicationDate = BoundCanonicalField(
            citation.PublicationDate,
            MaximumCanonicalIdentityFieldCharacters);
        var url = BoundCanonicalField(citation.Url, MaximumCanonicalUrlCharacters);
        var quotedText = retainQuotedText
            ? BoundCanonicalField(citation.QuotedText, maximumQuotedTextCharacters)
            : new BoundedCanonicalField(null, citation.QuotedText is not null);
        var wasAltered = source.IsTruncated || documentType.IsTruncated ||
            resolutionNumber.IsTruncated || title.IsTruncated || chapter.IsTruncated ||
            section.IsTruncated || article.IsTruncated || publicationDate.IsTruncated ||
            url.IsTruncated || quotedText.IsTruncated;
        var snapshot = new RegulatoryEvidenceCitation(
            Source: source.Value!,
            DocumentType: documentType.Value,
            ResolutionNumber: resolutionNumber.Value,
            Title: title.Value!,
            Chapter: chapter.Value,
            Section: section.Value,
            Article: article.Value,
            PublicationDate: publicationDate.Value,
            Url: url.Value,
            QuotedText: quotedText.Value);
        var identity = new CanonicalCitationIdentity(
            Source: NormalizeText(snapshot.Source),
            DocumentType: NormalizeText(snapshot.DocumentType),
            ResolutionNumber: NormalizeText(snapshot.ResolutionNumber),
            Title: NormalizeText(snapshot.Title),
            Chapter: NormalizeText(snapshot.Chapter),
            Section: NormalizeText(snapshot.Section),
            Article: NormalizeLocator(snapshot.Article),
            PublicationDate: NormalizeText(snapshot.PublicationDate),
            Url: NormalizeText(snapshot.Url),
            QuotedText: NormalizeText(snapshot.QuotedText));
        var rawQuoteFingerprint = CreateIdentityFingerprint(citation.QuotedText).Hash;
        var tieBreaker = CreateDomainSeparatedHash(
            "canonical-citation",
            snapshot.Source,
            snapshot.DocumentType,
            snapshot.ResolutionNumber,
            snapshot.Title,
            snapshot.Chapter,
            snapshot.Section,
            snapshot.Article,
            snapshot.PublicationDate,
            snapshot.Url,
            rawQuoteFingerprint);
        return new SanitizedCanonicalCitation(snapshot, identity, tieBreaker, wasAltered);
    }

    private static BoundedCanonicalField BoundCanonicalField(string? value, int maximumCharacters)
    {
        if (value is null || value.Length <= maximumCharacters)
        {
            return new BoundedCanonicalField(value, false);
        }

        return new BoundedCanonicalField(value[..maximumCharacters], true);
    }

    private static BoundaryLimitations CreateBoundaryLimitations(
        bool fieldsTruncated,
        bool metadataTruncated,
        bool citationsTruncated)
    {
        var limitations = new List<string>(3);
        var limitationCodes = new List<string>(3);
        if (fieldsTruncated)
        {
            limitationCodes.Add("canonical_field_truncated");
            limitations.Add("Algunos campos del contexto canónico CNV fueron truncados por límites de seguridad.");
        }

        if (metadataTruncated)
        {
            limitationCodes.Add("document_metadata_truncated");
            limitations.Add("Parte de los metadatos canónicos del documento CNV fue truncada o descartada por límites de seguridad.");
        }

        if (citationsTruncated)
        {
            limitationCodes.Add("document_citations_truncated");
            limitations.Add("Parte de las citas canónicas del documento CNV fue truncada, deduplicada o descartada por límites de seguridad.");
        }

        return new BoundaryLimitations(ReadOnly(limitations), ReadOnly(limitationCodes));
    }

    private static RegulatoryEvidenceCitation MapOriginalCitation(CnvRegulationCitation citation) =>
        new(
            citation.Source,
            citation.DocumentType,
            citation.ResolutionNumber,
            citation.Title,
            citation.Chapter,
            citation.Section,
            citation.Article,
            citation.PublicationDate,
            citation.Url,
            citation.QuotedText);

    private static (string Text, int OriginalLength, bool IsTruncated) Bound(
        string text,
        int maxCharacters)
    {
        var originalLength = text.Length;
        return originalLength > maxCharacters
            ? (text[..maxCharacters], originalLength, true)
            : (text, originalLength, false);
    }

    private static void AddLimitation(
        string dimension,
        string status,
        bool isTruncated,
        ICollection<string> limitations,
        ICollection<string> limitationCodes)
    {
        var statusLimitation = (dimension, status) switch
        {
            ("document", LegalCnvEnrichmentStageStatuses.Missing) =>
                ("document_missing", "No se pudo verificar el contexto canónico del documento CNV porque no fue encontrado."),
            ("document", LegalCnvEnrichmentStageStatuses.TimedOut) =>
                ("document_timed_out", "No se pudo verificar el contexto canónico del documento CNV porque la consulta agotó el tiempo de espera."),
            ("document", LegalCnvEnrichmentStageStatuses.Malformed) =>
                ("document_malformed", "No se pudo verificar el contexto canónico del documento CNV porque la respuesta recibida no fue válida."),
            ("document", LegalCnvEnrichmentStageStatuses.Failed) =>
                ("document_failed", "No se pudo verificar el contexto canónico del documento CNV debido a un error de recuperación."),
            ("document", LegalCnvEnrichmentStageStatuses.Conflict) =>
                ("document_conflict", "El contexto canónico del documento CNV presenta conflictos de identidad y requiere revisión humana."),
            ("article", LegalCnvEnrichmentStageStatuses.Missing) =>
                ("article_missing", "No se pudo verificar el contexto canónico del artículo CNV porque no fue encontrado."),
            ("article", LegalCnvEnrichmentStageStatuses.TimedOut) =>
                ("article_timed_out", "No se pudo verificar el contexto canónico del artículo CNV porque la consulta agotó el tiempo de espera."),
            ("article", LegalCnvEnrichmentStageStatuses.Malformed) =>
                ("article_malformed", "No se pudo verificar el contexto canónico del artículo CNV porque la respuesta recibida no fue válida."),
            ("article", LegalCnvEnrichmentStageStatuses.Failed) =>
                ("article_failed", "No se pudo verificar el contexto canónico del artículo CNV debido a un error de recuperación."),
            ("article", LegalCnvEnrichmentStageStatuses.Conflict) =>
                ("article_conflict", "El contexto canónico del artículo CNV presenta conflictos de identidad y requiere revisión humana."),
            _ => default
        };

        if (statusLimitation != default)
        {
            limitationCodes.Add(statusLimitation.Item1);
            limitations.Add(statusLimitation.Item2);
        }

        if (!isTruncated)
        {
            return;
        }

        limitationCodes.Add($"{dimension}_truncated");
        limitations.Add(dimension == "document"
            ? "El contexto canónico del documento CNV fue truncado por límites de tamaño."
            : "El contexto canónico del artículo CNV fue truncado por límites de tamaño.");
    }

    private static string DetermineFinalStatus(
        LegalCnvEnrichmentStageAudit document,
        LegalCnvEnrichmentStageAudit article)
    {
        if (document.Status == LegalCnvEnrichmentStageStatuses.Conflict ||
            article.Status == LegalCnvEnrichmentStageStatuses.Conflict)
        {
            return RegulatoryEvidenceEnrichmentStatuses.Conflict;
        }

        var selectedStages = new[] { document, article }
            .Where(stage => stage.Selected)
            .ToArray();
        var hasUsableStage = selectedStages.Any(stage =>
            stage.Status == LegalCnvEnrichmentStageStatuses.Succeeded);
        if (!hasUsableStage)
        {
            return RegulatoryEvidenceEnrichmentStatuses.Unavailable;
        }

        return selectedStages.All(stage =>
            stage.Status == LegalCnvEnrichmentStageStatuses.Succeeded)
            ? RegulatoryEvidenceEnrichmentStatuses.Verified
            : RegulatoryEvidenceEnrichmentStatuses.Partial;
    }

    private static void AppendBoundaryLimitations(
        IReadOnlyList<string> sourceLimitations,
        IReadOnlyList<string> sourceCodes,
        ICollection<string> limitations,
        ICollection<string> limitationCodes)
    {
        foreach (var limitation in sourceLimitations)
        {
            limitations.Add(limitation);
        }

        foreach (var code in sourceCodes)
        {
            limitationCodes.Add(code);
        }
    }

    private static DocumentLookupOutcome EmptyDocumentLookup(string status) =>
        new(
            Status: status,
            Snapshot: null,
            IdFingerprint: IdentityFingerprint.Empty,
            SourceFingerprint: IdentityFingerprint.Empty,
            ResolutionFingerprint: IdentityFingerprint.Empty,
            OriginalTextLength: null,
            IsTruncated: false,
            Limitations: Array.Empty<string>(),
            LimitationCodes: Array.Empty<string>());

    private static ArticleLookupOutcome EmptyArticleLookup(string status) =>
        new(
            Status: status,
            Snapshot: null,
            ArticleFingerprint: IdentityFingerprint.Empty,
            SourceFingerprint: IdentityFingerprint.Empty,
            ResolutionFingerprint: IdentityFingerprint.Empty,
            OriginalTextLength: null,
            IsTruncated: false,
            Limitations: Array.Empty<string>(),
            LimitationCodes: Array.Empty<string>());

    private static LegalCnvEnrichmentStageAudit StageAudit(
        bool selected,
        bool attempted,
        bool fromCache,
        string status,
        int? originalTextLength = null,
        bool isTruncated = false) =>
        new(selected, attempted, fromCache, status, originalTextLength, isTruncated);

    private void LogStage(
        string enrichmentId,
        int rank,
        string tool,
        LegalCnvEnrichmentStageAudit audit) =>
        _logger.LogInformation(
            "CNV enrichment {EnrichmentId} rank {Rank} tool {Tool} stage {Status}.",
            enrichmentId,
            rank,
            tool,
            audit.Status);

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static IdentityFingerprint CreateIdentityFingerprint(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? IdentityFingerprint.Empty
            : new IdentityFingerprint(
                true,
                CreateDomainSeparatedHash("identity", NormalizeText(value)));

    private static IdentityFingerprint CreateLocatorFingerprint(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? IdentityFingerprint.Empty
            : new IdentityFingerprint(
                true,
                CreateDomainSeparatedHash("locator", NormalizeLocator(value)));

    private static string CreateDomainSeparatedHash(
        string domain,
        CandidateIdentity identity) =>
        CreateDomainSeparatedHash(
            domain,
            identity.NormalizedDocumentId,
            identity.NormalizedLocator);

    private static string CreateDomainSeparatedHash(string domain, params string?[] components)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(domain));
        hash.AppendData([0]);
        Span<byte> lengthPrefix = stackalloc byte[sizeof(int)];
        foreach (var component in components)
        {
            var bytes = Encoding.UTF8.GetBytes(component ?? string.Empty);
            BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, bytes.Length);
            hash.AppendData(lengthPrefix);
            hash.AppendData(bytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static CnvRegulatoryHitEnrichmentResult EmptyResult() =>
        new(
            Array.Empty<RegulatoryEvidenceEnrichment>(),
            Array.Empty<LegalCnvEnrichmentAudit>(),
            Array.Empty<string>());

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());

    private static IReadOnlyList<string> ReadOnlyOrdinalDistinct(IEnumerable<string> values)
    {
        var distinct = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (seen.Add(value))
            {
                distinct.Add(value);
            }
        }

        return ReadOnly(distinct);
    }

    private sealed record Candidate(
        CnvRegulationSearchResult Result,
        CnvRegulationCitation PrimaryCitation,
        string RequestDocumentId,
        string? ArticleLocator,
        CandidateIdentity Identity,
        string AuditCandidateKey,
        string StableSnapshot,
        IReadOnlyList<int> ContributingQueryIndices);

    private sealed record CandidateIdentity(
        string NormalizedDocumentId,
        string NormalizedLocator);

    private sealed class CandidateGroup(Candidate representative, int queryIndex)
    {
        internal Candidate Representative { get; set; } = representative;

        internal SortedSet<int> ContributingQueryIndices { get; } = [queryIndex];
    }

    private sealed record DocumentLookupOutcome(
        string Status,
        RegulatoryCanonicalDocument? Snapshot,
        IdentityFingerprint IdFingerprint,
        IdentityFingerprint SourceFingerprint,
        IdentityFingerprint ResolutionFingerprint,
        int? OriginalTextLength,
        bool IsTruncated,
        IReadOnlyList<string> Limitations,
        IReadOnlyList<string> LimitationCodes);

    private sealed record ArticleLookupOutcome(
        string Status,
        RegulatoryCanonicalArticle? Snapshot,
        IdentityFingerprint ArticleFingerprint,
        IdentityFingerprint SourceFingerprint,
        IdentityFingerprint ResolutionFingerprint,
        int? OriginalTextLength,
        bool IsTruncated,
        IReadOnlyList<string> Limitations,
        IReadOnlyList<string> LimitationCodes);

    private readonly record struct IdentityFingerprint(bool HasValue, string Hash)
    {
        internal static IdentityFingerprint Empty { get; } = new(false, EmptySha256Hex);
    }

    private sealed record DocumentSnapshotBuildResult(
        RegulatoryCanonicalDocument Snapshot,
        IReadOnlyList<string> Limitations,
        IReadOnlyList<string> LimitationCodes);

    private sealed record ArticleSnapshotBuildResult(
        RegulatoryCanonicalArticle Snapshot,
        IReadOnlyList<string> Limitations,
        IReadOnlyList<string> LimitationCodes);

    private sealed record MetadataSnapshot(
        IReadOnlyDictionary<string, string> Metadata,
        bool WasAltered);

    private sealed record CitationSnapshot(
        IReadOnlyList<RegulatoryEvidenceCitation> Citations,
        bool WasAltered);

    private sealed record SanitizedCanonicalCitation(
        RegulatoryEvidenceCitation Citation,
        CanonicalCitationIdentity Identity,
        string TieBreaker,
        bool WasAltered);

    private sealed record CanonicalCitationIdentity(
        string Source,
        string DocumentType,
        string ResolutionNumber,
        string Title,
        string Chapter,
        string Section,
        string Article,
        string PublicationDate,
        string Url,
        string QuotedText);

    private readonly record struct BoundedCanonicalField(string? Value, bool IsTruncated);

    private sealed record BoundaryLimitations(
        IReadOnlyList<string> Limitations,
        IReadOnlyList<string> LimitationCodes);

    private sealed record DocumentEnrichmentOutcome(
        RegulatoryCanonicalDocument? Document,
        LegalCnvEnrichmentStageAudit Audit);

    private sealed record ArticleEnrichmentOutcome(
        RegulatoryCanonicalArticle? Article,
        LegalCnvEnrichmentStageAudit Audit);
}
