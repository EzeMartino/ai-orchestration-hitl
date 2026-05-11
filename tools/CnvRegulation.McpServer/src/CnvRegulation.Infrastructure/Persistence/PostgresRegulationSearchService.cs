using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Application.Search;
using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.InMemory;
using Dapper;

namespace CnvRegulation.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL full-text implementation for regulation search.
/// </summary>
public sealed class PostgresRegulationSearchService(
    RegulationDbConnectionFactory connectionFactory,
    IRegulationQueryExpander queryExpander) : IRegulationSearchService
{
    /// <inheritdoc />
    public async Task<SearchRegulationResponse> SearchAsync(
        SearchRegulationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = string.IsNullOrWhiteSpace(request.Query) ? string.Empty : request.Query.Trim();
        var limit = request.Limit <= 0 ? 5 : Math.Min(request.Limit, 25);
        var expansion = queryExpander.Expand(query);

        if (expansion.SearchQueries.Count == 0)
        {
            return new SearchRegulationResponse
            {
                Query = query,
                Results = [],
                Warnings = []
            };
        }

        await using var connection = await connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var chunkMatches = new List<QuerySearchResult>();
        foreach (var searchQuery in expansion.SearchQueries)
        {
            var parameters = CreateSearchParameters(request, searchQuery, limit);
            var chunkRows = await connection.QueryAsync<SearchRow>(new CommandDefinition(
                ChunkSearchSql,
                parameters,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            chunkMatches.AddRange(chunkRows.Select(row => new QuerySearchResult(MapSearchResult(row), searchQuery)));
        }

        var results = MergeSearchResults(chunkMatches, limit);

        if (results.Length == 0)
        {
            var documentMatches = new List<QuerySearchResult>();
            foreach (var searchQuery in expansion.SearchQueries)
            {
                var parameters = CreateSearchParameters(request, searchQuery, limit);
                var documentRows = await connection.QueryAsync<SearchRow>(new CommandDefinition(
                    DocumentSearchSql,
                    parameters,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

                documentMatches.AddRange(documentRows.Select(row => new QuerySearchResult(MapSearchResult(row), searchQuery)));
            }

            results = MergeSearchResults(documentMatches, limit);
        }

        var filteredMocks = results.Length == 0
            ? MockRegulationData.SearchResults
                .Where(result => MatchesMockFilters(result, request))
                .Take(limit)
                .ToArray()
            : [];

        return new SearchRegulationResponse
        {
            Query = query,
            Results = results.Concat(filteredMocks).Take(limit).ToArray(),
            Warnings = CreateWarnings(expansion, includeMockWarning: results.Length == 0)
        };
    }

    private const string FilterSql = """
        AND (CAST(@Source AS text) IS NULL OR d.source = CAST(@Source AS text))
        AND (CAST(@DocumentType AS text) IS NULL OR d.document_type = CAST(@DocumentType AS text))
        AND (CAST(@ResolutionNumber AS text) IS NULL OR d.resolution_number = CAST(@ResolutionNumber AS text))
        AND (CAST(@Status AS text) IS NULL OR d.status = CAST(@Status AS text))
        AND (CAST(@RequiresReview AS boolean) IS NULL OR d.requires_review = CAST(@RequiresReview AS boolean))
        AND (
            CAST(@AreaLike AS text) IS NULL
            OR d.title ILIKE CAST(@AreaLike AS text)
            OR d.document_type ILIKE CAST(@AreaLike AS text)
            OR d.text ILIKE CAST(@AreaLike AS text)
        )
        """;

    private const string ChunkSearchSql = $"""
        SELECT
            c.id AS ChunkId,
            c.document_id AS DocumentId,
            d.title AS DocumentTitle,
            c.title AS Title,
            c.chapter AS Chapter,
            c.section AS Section,
            c.article AS Article,
            c.text AS Text,
            d.source AS Source,
            d.document_type AS DocumentType,
            d.resolution_number AS ResolutionNumber,
            d.publication_date AS PublicationDate,
            d.url AS Url,
            ts_rank(
                to_tsvector('spanish', coalesce(c.text, '')),
                websearch_to_tsquery('spanish', @Query)
            ) AS Score
        FROM regulation_chunks c
        JOIN regulation_documents d ON d.id = c.document_id
        WHERE to_tsvector('spanish', coalesce(c.text, '')) @@ websearch_to_tsquery('spanish', @Query)
        {FilterSql}
        ORDER BY Score DESC, c.chunk_index ASC
        LIMIT @Limit;
        """;

    private const string DocumentSearchSql = $"""
        SELECT
            d.id || '-document' AS ChunkId,
            d.id AS DocumentId,
            d.title AS DocumentTitle,
            NULL AS Title,
            NULL AS Chapter,
            NULL AS Section,
            NULL AS Article,
            d.text AS Text,
            d.source AS Source,
            d.document_type AS DocumentType,
            d.resolution_number AS ResolutionNumber,
            d.publication_date AS PublicationDate,
            d.url AS Url,
            ts_rank(
                to_tsvector('spanish', coalesce(d.text, '')),
                websearch_to_tsquery('spanish', @Query)
            ) AS Score
        FROM regulation_documents d
        WHERE to_tsvector('spanish', coalesce(d.text, '')) @@ websearch_to_tsquery('spanish', @Query)
        {FilterSql}
        ORDER BY Score DESC, d.id ASC
        LIMIT @Limit;
        """;

    private static DynamicParameters CreateSearchParameters(
        SearchRegulationRequest request,
        string query,
        int limit)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Query", query);
        parameters.Add("Limit", limit);
        parameters.Add("Source", NormalizeFilter(request.Source));
        parameters.Add("DocumentType", NormalizeFilter(request.DocumentType));
        parameters.Add("ResolutionNumber", NormalizeFilter(request.ResolutionNumber));
        parameters.Add("Status", NormalizeFilter(request.Status));
        parameters.Add("RequiresReview", request.RequiresReview);
        parameters.Add("AreaLike", string.IsNullOrWhiteSpace(request.Area) ? null : $"%{request.Area.Trim()}%");

        return parameters;
    }

    private static RegulationSearchResult MapSearchResult(SearchRow row)
    {
        var snippet = CreateSnippet(row.Text);

        return new RegulationSearchResult
        {
            DocumentId = row.DocumentId,
            ChunkId = row.ChunkId,
            Title = row.DocumentTitle,
            Chapter = row.Chapter,
            Section = row.Section,
            Article = row.Article,
            Source = row.Source,
            Url = row.Url,
            Snippet = snippet,
            Score = row.Score,
            Citations =
            [
                new RegulationCitation
                {
                    Source = row.Source,
                    DocumentType = row.DocumentType,
                    ResolutionNumber = row.ResolutionNumber,
                    Title = row.DocumentTitle,
                    Chapter = row.Chapter,
                    Section = row.Section,
                    Article = row.Article,
                    PublicationDate = row.PublicationDate,
                    Url = row.Url,
                    QuotedText = snippet
                }
            ]
        };
    }

    private static RegulationSearchResult[] MergeSearchResults(
        IReadOnlyList<QuerySearchResult> matches,
        int limit) =>
        matches
            .GroupBy(match => match.Result.ChunkId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var best = group
                    .OrderByDescending(match => match.Result.Score)
                    .First()
                    .Result;
                var score = group.Max(match => match.Result.Score) + ((group.Count() - 1) * 0.05);

                return CopyWithScore(best, score);
            })
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.ChunkId, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();

    private static RegulationSearchResult CopyWithScore(RegulationSearchResult result, double score) =>
        new()
        {
            DocumentId = result.DocumentId,
            ChunkId = result.ChunkId,
            Title = result.Title,
            Chapter = result.Chapter,
            Section = result.Section,
            Article = result.Article,
            Source = result.Source,
            Url = result.Url,
            Snippet = result.Snippet,
            Score = score,
            Citations = result.Citations
        };

    private static bool MatchesMockFilters(RegulationSearchResult result, SearchRegulationRequest request)
    {
        var citation = result.Citations.FirstOrDefault();

        return MatchesOptional(result.Source, request.Source)
            && MatchesOptional(citation?.DocumentType, request.DocumentType)
            && MatchesOptional(citation?.ResolutionNumber, request.ResolutionNumber)
            && request.Status is null
            && request.RequiresReview is null;
    }

    private static bool MatchesOptional(string? actual, string? expected)
    {
        return string.IsNullOrWhiteSpace(expected)
            || string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

    private sealed class SearchRow
    {
        public required string ChunkId { get; init; }

        public required string DocumentId { get; init; }

        public required string DocumentTitle { get; init; }

        public string? Chapter { get; init; }

        public string? Section { get; init; }

        public string? Article { get; init; }

        public required string Text { get; init; }

        public required string Source { get; init; }

        public required string DocumentType { get; init; }

        public string? ResolutionNumber { get; init; }

        public DateOnly? PublicationDate { get; init; }

        public required string Url { get; init; }

        public double Score { get; init; }
    }

    private sealed record QuerySearchResult(RegulationSearchResult Result, string SearchQuery);
}
