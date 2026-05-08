using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.InMemory;
using Dapper;

namespace CnvRegulation.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL full-text implementation for regulation search.
/// </summary>
public sealed class PostgresRegulationSearchService(
    RegulationDbConnectionFactory connectionFactory) : IRegulationSearchService
{
    /// <inheritdoc />
    public async Task<SearchRegulationResponse> SearchAsync(
        SearchRegulationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = string.IsNullOrWhiteSpace(request.Query) ? string.Empty : request.Query.Trim();
        var limit = request.Limit <= 0 ? 5 : Math.Min(request.Limit, 25);

        if (string.IsNullOrWhiteSpace(query))
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

        var parameters = CreateSearchParameters(request, query, limit);
        var chunkRows = await connection.QueryAsync<SearchRow>(new CommandDefinition(
            ChunkSearchSql,
            parameters,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var results = chunkRows
            .Select(MapSearchResult)
            .ToArray();

        if (results.Length == 0)
        {
            var documentRows = await connection.QueryAsync<SearchRow>(new CommandDefinition(
                DocumentSearchSql,
                parameters,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            results = documentRows
                .Select(MapSearchResult)
                .ToArray();
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
            Warnings = results.Length == 0 ? [MockRegulationData.MockWarning] : []
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
                plainto_tsquery('spanish', @Query)
            ) AS Score
        FROM regulation_chunks c
        JOIN regulation_documents d ON d.id = c.document_id
        WHERE to_tsvector('spanish', coalesce(c.text, '')) @@ plainto_tsquery('spanish', @Query)
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
                plainto_tsquery('spanish', @Query)
            ) AS Score
        FROM regulation_documents d
        WHERE to_tsvector('spanish', coalesce(d.text, '')) @@ plainto_tsquery('spanish', @Query)
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
}
