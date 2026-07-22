using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Legal.Cnv;
using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

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
        var documentCache = new Dictionary<string, DocumentRawOutcome>(StringComparer.Ordinal);

        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = candidates[index];
            var rank = index + 1;
            var enrichmentId = CreateEnrichmentId(candidate.CandidateKey);
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
            var immutableLimitations = ReadOnly(limitations);
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
                CandidateKey: candidate.CandidateKey,
                Score: candidate.Result.Score,
                ContributingQueryIndices: candidate.ContributingQueryIndices,
                Document: document.Audit,
                Article: article.Audit,
                Status: status,
                LimitationCodes: ReadOnly(limitationCodes)));

            foreach (var limitation in limitations)
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
        IDictionary<string, DocumentRawOutcome> cache,
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
        DocumentRawOutcome rawOutcome;
        var fromCache = cache.TryGetValue(candidate.NormalizedDocumentId, out var cachedOutcome);
        if (fromCache)
        {
            rawOutcome = cachedOutcome!;
        }
        else
        {
            rawOutcome = await RetrieveDocumentAsync(candidate.RequestDocumentId, cancellationToken)
                .ConfigureAwait(false);
            cache[candidate.NormalizedDocumentId] = rawOutcome;
        }

        RegulatoryCanonicalDocument? document = null;
        var status = rawOutcome.Status;
        int? originalLength = null;
        var isTruncated = false;

        if (status == LegalCnvEnrichmentStageStatuses.Succeeded)
        {
            var response = rawOutcome.Response!;
            var canonical = response.Document!;
            var bounded = Bound(canonical.Text, _options.MaxDocumentContextCharacters);
            document = MapDocument(
                canonical,
                response.Citations!,
                bounded,
                _options.MaxDocumentContextCharacters);
            originalLength = bounded.OriginalLength;
            isTruncated = bounded.IsTruncated;

            if (HasDocumentConflict(candidate, canonical))
            {
                status = LegalCnvEnrichmentStageStatuses.Conflict;
            }
        }

        AddLimitation("document", status, isTruncated, limitations, limitationCodes);
        var audit = StageAudit(
            selected: true,
            attempted: !fromCache,
            fromCache,
            status,
            originalLength,
            isTruncated);
        LogStage(enrichmentId, rank, "get_document", audit);
        return new DocumentEnrichmentOutcome(document, audit);
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
        var rawOutcome = await RetrieveArticleAsync(request, cancellationToken).ConfigureAwait(false);

        RegulatoryCanonicalArticle? article = null;
        var status = rawOutcome.Status;
        int? originalLength = null;
        var isTruncated = false;
        if (status == LegalCnvEnrichmentStageStatuses.Succeeded)
        {
            var response = rawOutcome.Response!;
            var bounded = Bound(response.Text!, _options.MaxArticleContextCharacters);
            article = new RegulatoryCanonicalArticle(
                Citation: MapCanonicalCitation(response.Citation!, _options.MaxArticleContextCharacters),
                Text: bounded.Text,
                Confidence: response.Confidence,
                OriginalTextLength: bounded.OriginalLength,
                IsTruncated: bounded.IsTruncated);
            originalLength = bounded.OriginalLength;
            isTruncated = bounded.IsTruncated;

            if (HasArticleConflict(candidate, response.Citation!))
            {
                status = LegalCnvEnrichmentStageStatuses.Conflict;
            }
        }

        AddLimitation("article", status, isTruncated, limitations, limitationCodes);
        var audit = StageAudit(
            selected: true,
            attempted: true,
            fromCache: false,
            status,
            originalLength,
            isTruncated);
        LogStage(enrichmentId, rank, "get_article", audit);
        return new ArticleEnrichmentOutcome(article, audit);
    }

    private async Task<DocumentRawOutcome> RetrieveDocumentAsync(
        string documentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetDocumentAsync(
                new CnvRegulationDocumentRequest(documentId),
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return ValidateDocumentResponse(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new DocumentRawOutcome(LegalCnvEnrichmentStageStatuses.TimedOut, null);
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new DocumentRawOutcome(LegalCnvEnrichmentStageStatuses.Failed, null);
        }
    }

    private async Task<ArticleRawOutcome> RetrieveArticleAsync(
        CnvRegulationArticleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetArticleAsync(request, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return ValidateArticleResponse(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ArticleRawOutcome(LegalCnvEnrichmentStageStatuses.TimedOut, null);
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ArticleRawOutcome(LegalCnvEnrichmentStageStatuses.Failed, null);
        }
    }

    private static DocumentRawOutcome ValidateDocumentResponse(CnvRegulationDocumentResponse? response)
    {
        if (response is null || response.Warnings is null || response.Citations is null)
        {
            return new DocumentRawOutcome(LegalCnvEnrichmentStageStatuses.Malformed, null);
        }

        if (!response.Found)
        {
            return response.Document is null
                ? new DocumentRawOutcome(LegalCnvEnrichmentStageStatuses.Missing, null)
                : new DocumentRawOutcome(LegalCnvEnrichmentStageStatuses.Malformed, null);
        }

        if (response.Document is null ||
            string.IsNullOrWhiteSpace(response.Document.Id) ||
            string.IsNullOrWhiteSpace(response.Document.Text))
        {
            return new DocumentRawOutcome(LegalCnvEnrichmentStageStatuses.Malformed, null);
        }

        return new DocumentRawOutcome(LegalCnvEnrichmentStageStatuses.Succeeded, response);
    }

    private static ArticleRawOutcome ValidateArticleResponse(CnvRegulationArticleResponse? response)
    {
        if (response is null || response.Warnings is null)
        {
            return new ArticleRawOutcome(LegalCnvEnrichmentStageStatuses.Malformed, null);
        }

        if (!response.Found)
        {
            return response.Text is null && response.Citation is null
                ? new ArticleRawOutcome(LegalCnvEnrichmentStageStatuses.Missing, null)
                : new ArticleRawOutcome(LegalCnvEnrichmentStageStatuses.Malformed, null);
        }

        if (string.IsNullOrWhiteSpace(response.Text) ||
            response.Citation is null ||
            string.IsNullOrWhiteSpace(response.Citation.Article))
        {
            return new ArticleRawOutcome(LegalCnvEnrichmentStageStatuses.Malformed, null);
        }

        return new ArticleRawOutcome(LegalCnvEnrichmentStageStatuses.Succeeded, response);
    }

    private static IReadOnlyList<Candidate> SelectCandidates(
        IReadOnlyList<CnvRegulationEnrichmentHit> hits,
        int maximumHits)
    {
        var groups = new Dictionary<string, CandidateGroup>(StringComparer.Ordinal);
        foreach (var hit in hits)
        {
            if (!TryCreateCandidate(hit, out var candidate))
            {
                continue;
            }

            if (!groups.TryGetValue(candidate.CandidateKey, out var group))
            {
                groups[candidate.CandidateKey] = new CandidateGroup(candidate, hit!.QueryIndex);
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
            .ThenBy(candidate => candidate.NormalizedDocumentId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.NormalizedLocator, StringComparer.Ordinal)
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
        var candidateKey = $"{normalizedDocumentId}|{normalizedLocator}";
        var requestDocumentId = result.DocumentId.Trim();
        var articleLocator = FirstNonBlank(primaryCitation.Article, result.Article);
        candidate = new Candidate(
            Result: result,
            PrimaryCitation: primaryCitation,
            RequestDocumentId: requestDocumentId,
            ArticleLocator: articleLocator,
            NormalizedDocumentId: normalizedDocumentId,
            NormalizedLocator: normalizedLocator,
            CandidateKey: candidateKey,
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

    private static bool HasDocumentConflict(Candidate candidate, CnvRegulationDocument document) =>
        !StringComparer.Ordinal.Equals(
            candidate.NormalizedDocumentId,
            NormalizeText(document.Id)) ||
        ValuesConflict(candidate.PrimaryCitation.Source, document.Source) ||
        ValuesConflict(candidate.PrimaryCitation.ResolutionNumber, document.ResolutionNumber);

    private static bool HasArticleConflict(Candidate candidate, CnvRegulationCitation citation) =>
        !StringComparer.Ordinal.Equals(
            NormalizeLocator(candidate.ArticleLocator),
            NormalizeLocator(citation.Article)) ||
        ValuesConflict(candidate.PrimaryCitation.Source, citation.Source) ||
        ValuesConflict(candidate.PrimaryCitation.ResolutionNumber, citation.ResolutionNumber);

    private static bool ValuesConflict(string? original, string? canonical) =>
        !string.IsNullOrWhiteSpace(original) &&
        !string.IsNullOrWhiteSpace(canonical) &&
        !StringComparer.Ordinal.Equals(NormalizeText(original), NormalizeText(canonical));

    private static RegulatoryCanonicalDocument MapDocument(
        CnvRegulationDocument document,
        IReadOnlyList<CnvRegulationCitation> citations,
        (string Text, int OriginalLength, bool IsTruncated) bounded,
        int maximumQuotedTextCharacters)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (document.Metadata is not null)
        {
            foreach (var pair in document.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (pair.Key is not null)
                {
                    metadata[pair.Key] = pair.Value ?? string.Empty;
                }
            }
        }

        var mappedCitations = citations
            .Where(citation => citation is not null)
            .Select(citation => MapCanonicalCitation(citation, maximumQuotedTextCharacters))
            .ToArray();

        return new RegulatoryCanonicalDocument(
            Id: document.Id,
            Source: document.Source,
            DocumentType: document.DocumentType,
            ResolutionNumber: document.ResolutionNumber,
            Title: document.Title,
            PublicationDate: document.PublicationDate,
            EffectiveDate: document.EffectiveDate,
            Url: document.Url,
            Status: document.Status,
            RequiresReview: document.RequiresReview,
            RetrievedAt: document.RetrievedAt,
            Text: bounded.Text,
            OriginalTextLength: bounded.OriginalLength,
            IsTruncated: bounded.IsTruncated,
            Metadata: new ReadOnlyDictionary<string, string>(metadata),
            Citations: Array.AsReadOnly(mappedCitations));
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

    private static RegulatoryEvidenceCitation MapCanonicalCitation(
        CnvRegulationCitation citation,
        int maximumQuotedTextCharacters)
    {
        var quotedText = citation.QuotedText;
        if (quotedText is not null && quotedText.Length > maximumQuotedTextCharacters)
        {
            quotedText = quotedText[..maximumQuotedTextCharacters];
        }

        return new RegulatoryEvidenceCitation(
            citation.Source,
            citation.DocumentType,
            citation.ResolutionNumber,
            citation.Title,
            citation.Chapter,
            citation.Section,
            citation.Article,
            citation.PublicationDate,
            citation.Url,
            quotedText);
    }

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

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or
                UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string NormalizeLocator(string? value)
    {
        var normalized = NormalizeText(value);
        if (normalized.Length == 0)
        {
            return normalized;
        }

        var builder = new StringBuilder(normalized.Length);
        var pendingSpace = false;
        foreach (var character in normalized)
        {
            if (char.IsPunctuation(character))
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string CreateEnrichmentId(string candidateKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(candidateKey))).ToLowerInvariant();

    private static CnvRegulatoryHitEnrichmentResult EmptyResult() =>
        new(
            Array.Empty<RegulatoryEvidenceEnrichment>(),
            Array.Empty<LegalCnvEnrichmentAudit>(),
            Array.Empty<string>());

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());

    private sealed record Candidate(
        CnvRegulationSearchResult Result,
        CnvRegulationCitation PrimaryCitation,
        string RequestDocumentId,
        string? ArticleLocator,
        string NormalizedDocumentId,
        string NormalizedLocator,
        string CandidateKey,
        string StableSnapshot,
        IReadOnlyList<int> ContributingQueryIndices);

    private sealed class CandidateGroup(Candidate representative, int queryIndex)
    {
        internal Candidate Representative { get; set; } = representative;

        internal SortedSet<int> ContributingQueryIndices { get; } = [queryIndex];
    }

    private sealed record DocumentRawOutcome(
        string Status,
        CnvRegulationDocumentResponse? Response);

    private sealed record ArticleRawOutcome(
        string Status,
        CnvRegulationArticleResponse? Response);

    private sealed record DocumentEnrichmentOutcome(
        RegulatoryCanonicalDocument? Document,
        LegalCnvEnrichmentStageAudit Audit);

    private sealed record ArticleEnrichmentOutcome(
        RegulatoryCanonicalArticle? Article,
        LegalCnvEnrichmentStageAudit Audit);
}
