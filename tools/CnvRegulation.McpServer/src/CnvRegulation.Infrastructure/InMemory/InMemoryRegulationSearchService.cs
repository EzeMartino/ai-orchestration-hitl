using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;

namespace CnvRegulation.Infrastructure.InMemory;

/// <summary>
/// In-memory search implementation for locally ingested and mock CNV documents.
/// </summary>
public sealed class InMemoryRegulationSearchService(
    IRegulationRepository repository,
    IRegulationChunkRepository chunkRepository) : IRegulationSearchService
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

        var documents = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var chunks = await chunkRepository.ListChunksAsync(cancellationToken).ConfigureAwait(false);
        var documentLookup = documents.ToDictionary(document => document.Id, StringComparer.OrdinalIgnoreCase);
        var chunkDocumentIds = chunks
            .Select(chunk => chunk.DocumentId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var chunkResults = chunks
            .Where(chunk => documentLookup.ContainsKey(chunk.DocumentId))
            .Select(chunk => new { Chunk = chunk, Document = documentLookup[chunk.DocumentId] })
            .Where(item => MatchesChunk(item.Chunk, item.Document, query, request))
            .Select(item => CreateSearchResult(item.Chunk, item.Document));

        var documentResults = documents
            .Where(document => !chunkDocumentIds.Contains(document.Id))
            .Where(document => MatchesDocument(document, query, request))
            .Select(CreateSearchResult);

        var mockResults = MockRegulationData.SearchResults
            .Where(result => MatchesResultFilters(result, request))
            .Where(result => MatchesArea(result, request.Area));

        var results = chunkResults
            .Concat(documentResults)
            .Concat(mockResults)
            .Take(limit)
            .ToArray();

        return new SearchRegulationResponse
        {
            Query = query,
            Results = results,
            Warnings = [MockRegulationData.MockWarning]
        };
    }

    private static bool MatchesDocument(RegulationDocument document, string query, SearchRegulationRequest request)
    {
        return MatchesDocumentArea(document, request.Area)
            && MatchesDocumentQuery(document, query)
            && MatchesDocumentFilters(document, request);
    }

    private static bool MatchesChunk(RegulationChunk chunk, RegulationDocument document, string query, SearchRegulationRequest request)
    {
        return MatchesChunkArea(chunk, document, request.Area)
            && MatchesChunkQuery(chunk, document, query)
            && MatchesDocumentFilters(document, request);
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

    private static bool MatchesDocumentQuery(RegulationDocument document, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return document.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || document.Text.Contains(query, StringComparison.OrdinalIgnoreCase)
            || document.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (document.ResolutionNumber?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
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

    private static bool MatchesChunkQuery(RegulationChunk chunk, RegulationDocument document, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return chunk.Text.Contains(query, StringComparison.OrdinalIgnoreCase)
            || chunk.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
            || document.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || document.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (chunk.Article?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
            || (document.ResolutionNumber?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
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

    private static RegulationSearchResult CreateSearchResult(RegulationDocument document)
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
            Score = 0.95,
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

    private static RegulationSearchResult CreateSearchResult(RegulationChunk chunk, RegulationDocument document)
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
            Score = 0.81,
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
}
