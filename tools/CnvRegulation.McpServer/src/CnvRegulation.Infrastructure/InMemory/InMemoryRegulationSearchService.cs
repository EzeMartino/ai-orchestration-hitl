using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Application.Search;
using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.Search;

namespace CnvRegulation.Infrastructure.InMemory;

/// <summary>
/// In-memory search implementation for locally ingested and mock CNV documents.
/// </summary>
public sealed class InMemoryRegulationSearchService(
    IRegulationRepository repository,
    IRegulationChunkRepository chunkRepository,
    IRegulationQueryExpander queryExpander) : IRegulationSearchService
{
    /// <inheritdoc />
    public async Task<SearchRegulationResponse> SearchAsync(
        SearchRegulationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var limit = request.Limit <= 0 ? 5 : Math.Min(request.Limit, 25);
        var query = string.IsNullOrWhiteSpace(request.Query) ? string.Empty : request.Query.Trim();
        var expansion = queryExpander.Expand(query);

        var documents = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var chunks = await chunkRepository.ListChunksAsync(cancellationToken).ConfigureAwait(false);
        var documentLookup = documents.ToDictionary(document => document.Id, StringComparer.OrdinalIgnoreCase);
        var chunkDocumentIds = chunks
            .Select(chunk => chunk.DocumentId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var chunkResults = chunks
            .Where(chunk => documentLookup.ContainsKey(chunk.DocumentId))
            .Select(chunk => new { Chunk = chunk, Document = documentLookup[chunk.DocumentId] })
            .Where(item => MatchesChunk(item.Chunk, item.Document, expansion, request))
            .Select(item => CreateCandidate(
                CreateSearchResult(
                    item.Chunk,
                    item.Document,
                    0.81 + SourcePriorityScorer.Score(item.Document, item.Chunk) - (item.Chunk.DuplicateOfChunkId is null ? 0 : 0.20)),
                item.Chunk.ContentHash,
                item.Chunk.DuplicateOfChunkId is not null));

        var documentResults = documents
            .Where(document => !chunkDocumentIds.Contains(document.Id))
            .Where(document => MatchesDocument(document, expansion, request))
            .Select(document => CreateCandidate(
                CreateSearchResult(document, 0.95 + SourcePriorityScorer.Score(document)),
                contentHash: null,
                isDuplicate: false));

        var localCandidates = chunkResults
            .Concat(documentResults)
            .ToArray();
        var mockResults = localCandidates.Length == 0
            ? MockRegulationData.SearchResults
                .Where(result => MatchesResultFilters(result, request))
                .Where(result => MatchesArea(result, request.Area))
                .Select(result => CreateCandidate(result, contentHash: null, isDuplicate: false))
            : [];
        var candidates = localCandidates
            .Concat(mockResults)
            .ToArray();
        var results = SelectSearchResults(candidates, request.IncludeDuplicates, limit)
            .Take(limit)
            .ToArray();

        return new SearchRegulationResponse
        {
            Query = query,
            Results = results,
            Warnings = CreateWarnings(expansion, includeMockWarning: true)
        };
    }

    private static bool MatchesDocument(
        RegulationDocument document,
        RegulationQueryExpansion expansion,
        SearchRegulationRequest request)
    {
        return MatchesDocumentArea(document, request.Area)
            && MatchesDocumentQuery(document, expansion)
            && MatchesDocumentFilters(document, request)
            && (request.IncludeNonSearchable || !SourcePriorityScorer.IsNonSearchable(document));
    }

    private static bool MatchesChunk(
        RegulationChunk chunk,
        RegulationDocument document,
        RegulationQueryExpansion expansion,
        SearchRegulationRequest request)
    {
        return MatchesChunkArea(chunk, document, request.Area)
            && MatchesChunkQuery(chunk, document, expansion)
            && MatchesDocumentFilters(document, request)
            && (request.IncludeNonSearchable || !SourcePriorityScorer.IsNonSearchable(document));
    }

    private static bool MatchesDocumentArea(RegulationDocument document, string? area)
    {
        if (string.IsNullOrWhiteSpace(area))
        {
            return true;
        }

        return document.Title.Contains(area, StringComparison.OrdinalIgnoreCase)
            || document.Text.Contains(area, StringComparison.OrdinalIgnoreCase)
            || document.DocumentType.Contains(area, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDocumentQuery(RegulationDocument document, RegulationQueryExpansion expansion)
    {
        if (expansion.SearchQueries.Count == 0)
        {
            return true;
        }

        var searchableText = string.Join(
            ' ',
            document.Title,
            document.Text,
            document.Id,
            document.ResolutionNumber);

        return expansion.SearchQueries.Any(searchQuery => MatchesTextQuery(searchableText, searchQuery));
    }

    private static bool MatchesChunkArea(RegulationChunk chunk, RegulationDocument document, string? area)
    {
        if (string.IsNullOrWhiteSpace(area))
        {
            return true;
        }

        return document.Title.Contains(area, StringComparison.OrdinalIgnoreCase)
            || document.DocumentType.Contains(area, StringComparison.OrdinalIgnoreCase)
            || chunk.Text.Contains(area, StringComparison.OrdinalIgnoreCase)
            || (chunk.Title?.Contains(area, StringComparison.OrdinalIgnoreCase) ?? false)
            || (chunk.Chapter?.Contains(area, StringComparison.OrdinalIgnoreCase) ?? false)
            || (chunk.Section?.Contains(area, StringComparison.OrdinalIgnoreCase) ?? false)
            || (chunk.Article?.Contains(area, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool MatchesChunkQuery(
        RegulationChunk chunk,
        RegulationDocument document,
        RegulationQueryExpansion expansion)
    {
        if (expansion.SearchQueries.Count == 0)
        {
            return true;
        }

        var searchableText = string.Join(
            ' ',
            chunk.Text,
            chunk.Id,
            document.Title,
            document.Id,
            chunk.Article,
            document.ResolutionNumber);

        return expansion.SearchQueries.Any(searchQuery => MatchesTextQuery(searchableText, searchQuery));
    }

    private static bool MatchesArea(RegulationSearchResult result, string? area)
    {
        if (string.IsNullOrWhiteSpace(area))
        {
            return true;
        }

        return result.Snippet.Contains(area, StringComparison.OrdinalIgnoreCase)
            || result.Title.Contains(area, StringComparison.OrdinalIgnoreCase)
            || string.Equals(area, "Agentes", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDocumentFilters(RegulationDocument document, SearchRegulationRequest request)
    {
        return MatchesOptional(document.Source, request.Source)
            && MatchesOptional(document.DocumentType, request.DocumentType)
            && MatchesOptional(document.ResolutionNumber, request.ResolutionNumber)
            && MatchesOptional(document.Status, request.Status)
            && (request.RequiresReview is null || document.RequiresReview == request.RequiresReview);
    }

    private static bool MatchesResultFilters(RegulationSearchResult result, SearchRegulationRequest request)
    {
        return MatchesOptional(result.Source, request.Source)
            && MatchesOptional(result.Citations.FirstOrDefault()?.DocumentType, request.DocumentType)
            && MatchesOptional(result.Citations.FirstOrDefault()?.ResolutionNumber, request.ResolutionNumber)
            && request.Status is null
            && request.RequiresReview is null;
    }

    private static bool MatchesOptional(string? actual, string? expected)
    {
        return string.IsNullOrWhiteSpace(expected)
            || string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static RegulationSearchResult CreateSearchResult(RegulationDocument document, double score)
    {
        var snippet = CreateSnippet(document.Text);

        return new RegulationSearchResult
        {
            DocumentId = document.Id,
            ChunkId = $"{document.Id}-local-document",
            Title = document.Title,
            Source = document.Source,
            Url = document.Url,
            Snippet = snippet,
            Score = score,
            Citations =
            [
                new RegulationCitation
                {
                    Source = document.Source,
                    DocumentType = document.DocumentType,
                    ResolutionNumber = document.ResolutionNumber,
                    Title = document.Title,
                    PublicationDate = document.PublicationDate,
                    Url = document.Url,
                    QuotedText = snippet
                }
            ]
        };
    }

    private static RegulationSearchResult CreateSearchResult(RegulationChunk chunk, RegulationDocument document, double score)
    {
        var snippet = CreateSnippet(chunk.Text);

        return new RegulationSearchResult
        {
            DocumentId = document.Id,
            ChunkId = chunk.Id,
            Title = document.Title,
            Chapter = chunk.Chapter,
            Section = chunk.Section,
            Article = chunk.Article,
            Source = document.Source,
            Url = document.Url,
            Snippet = snippet,
            Score = score,
            Citations =
            [
                new RegulationCitation
                {
                    Source = document.Source,
                    DocumentType = document.DocumentType,
                    ResolutionNumber = document.ResolutionNumber,
                    Title = document.Title,
                    Chapter = chunk.Chapter,
                    Section = chunk.Section,
                    Article = chunk.Article,
                    PublicationDate = document.PublicationDate,
                    Url = document.Url,
                    QuotedText = snippet
                }
            ]
        };
    }

    private static string CreateSnippet(string text)
    {
        const int maxLength = 220;

        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text.Length <= maxLength ? text : $"{text[..maxLength]}...";
    }

    private static IReadOnlyList<string> CreateWarnings(
        RegulationQueryExpansion expansion,
        bool includeMockWarning)
    {
        var warnings = new List<string>();
        if (includeMockWarning)
        {
            warnings.Add(MockRegulationData.MockWarning);
        }

        if (expansion.ExpandedTerms.Count > 0)
        {
            warnings.Add($"Query expansion applied. Expanded terms: {string.Join("; ", expansion.ExpandedTerms)}");
            warnings.Add($"Expanded search queries: {string.Join(" | ", expansion.SearchQueries)}");
        }

        return warnings;
    }

    private static bool MatchesTextQuery(string text, string query)
    {
        var normalizedText = StaticRegulationQueryExpander.Normalize(text);
        var normalizedQuery = StaticRegulationQueryExpander.Normalize(query);
        if (normalizedQuery.Length == 0)
        {
            return true;
        }

        if (normalizedText.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var queryTerms = normalizedQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length > 2)
            .Where(term => !IsStopTerm(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return queryTerms.Length > 0
            && queryTerms.All(term => normalizedText.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsStopTerm(string term) =>
        term is "del" or "las" or "los" or "una" or "uno" or "para" or "con";

    private static SearchCandidate CreateCandidate(
        RegulationSearchResult result,
        string? contentHash,
        bool isDuplicate) =>
        new(result, contentHash, isDuplicate);

    private static IReadOnlyList<RegulationSearchResult> SelectSearchResults(
        IReadOnlyList<SearchCandidate> candidates,
        bool includeDuplicates,
        int limit)
    {
        var selectedCandidates = includeDuplicates
            ? candidates
            : candidates
                .GroupBy(candidate => string.IsNullOrWhiteSpace(candidate.ContentHash)
                    ? candidate.Result.ChunkId
                    : candidate.ContentHash, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(candidate => candidate.Result.Score)
                    .ThenBy(candidate => candidate.IsDuplicate)
                    .ThenBy(candidate => candidate.Result.ChunkId, StringComparer.OrdinalIgnoreCase)
                    .First())
                .ToArray();

        return selectedCandidates
            .OrderByDescending(candidate => candidate.Result.Score)
            .ThenBy(candidate => candidate.Result.ChunkId, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(candidate => candidate.Result)
            .ToArray();
    }

    private sealed record SearchCandidate(RegulationSearchResult Result, string? ContentHash, bool IsDuplicate);
}
