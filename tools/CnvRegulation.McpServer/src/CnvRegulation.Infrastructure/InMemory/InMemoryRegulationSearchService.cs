using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;

namespace CnvRegulation.Infrastructure.InMemory;

/// <summary>
/// In-memory search implementation for locally ingested and mock CNV documents.
/// </summary>
public sealed class InMemoryRegulationSearchService(IRegulationRepository repository) : IRegulationSearchService
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

        var ingestedResults = (await repository.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(document => MatchesDocument(document, query, request.Area))
            .Select(CreateSearchResult);

        var mockResults = MockRegulationData.SearchResults
            .Where(result => MatchesArea(result, request.Area));

        var results = ingestedResults
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

    private static bool MatchesDocument(RegulationDocument document, string query, string? area)
    {
        return MatchesDocumentArea(document, area) && MatchesDocumentQuery(document, query);
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
