using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Application.Search;
using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.InMemory;
using CnvRegulation.Infrastructure.Search;
using Dapper;

namespace CnvRegulation.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL full-text implementation for regulation search.
/// </summary>
public sealed class PostgresRegulationSearchService(
    RegulationDbConnectionFactory connectionFactory,
    IRegulationQueryExpander queryExpander,
    IEmbeddingGenerator? embeddingGenerator = null) : IRegulationSearchService
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

        var mode = NormalizeSearchMode(request.SearchMode);
        var warnings = new List<string>(CreateWarnings(expansion, includeMockWarning: false));
        var results = mode switch
        {
            "semantic" => await SearchSemanticAsync(connection, request, expansion, limit, warnings, cancellationToken).ConfigureAwait(false),
            "hybrid" => await SearchHybridAsync(connection, request, expansion, limit, warnings, cancellationToken).ConfigureAwait(false),
            _ => await SearchFullTextAsync(connection, request, expansion, limit, cancellationToken).ConfigureAwait(false)
        };

        var includeMockWarning = results.Length == 0;
        if (includeMockWarning)
        {
            warnings.Insert(0, MockRegulationData.MockWarning);
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
            Warnings = warnings
        };
    }

    private const string FilterSql = """
        AND (CAST(@Source AS text) IS NULL OR d.source = CAST(@Source AS text))
        AND (CAST(@DocumentType AS text) IS NULL OR d.document_type = CAST(@DocumentType AS text))
        AND (CAST(@ResolutionNumber AS text) IS NULL OR d.resolution_number = CAST(@ResolutionNumber AS text))
        AND (CAST(@Status AS text) IS NULL OR d.status = CAST(@Status AS text))
        AND (CAST(@RequiresReview AS boolean) IS NULL OR d.requires_review = CAST(@RequiresReview AS boolean))
        AND (CAST(@IncludeNonSearchable AS boolean) OR COALESCE(d.metadata->>'searchable', 'true') <> 'false')
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
            c.content_hash AS ContentHash,
            c.duplicate_of_chunk_id AS DuplicateOfChunkId,
            d.source AS Source,
            d.document_type AS DocumentType,
            d.resolution_number AS ResolutionNumber,
            d.publication_date AS PublicationDate,
            d.url AS Url,
            d.status AS Status,
            d.metadata->>'sourceFile' AS SourceFile,
            d.metadata->>'searchable' AS Searchable,
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
            NULL AS ContentHash,
            NULL AS DuplicateOfChunkId,
            d.source AS Source,
            d.document_type AS DocumentType,
            d.resolution_number AS ResolutionNumber,
            d.publication_date AS PublicationDate,
            d.url AS Url,
            d.status AS Status,
            d.metadata->>'sourceFile' AS SourceFile,
            d.metadata->>'searchable' AS Searchable,
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

    private const string SemanticChunkSearchSql = $"""
        SELECT
            c.id AS ChunkId,
            c.document_id AS DocumentId,
            d.title AS DocumentTitle,
            c.title AS Title,
            c.chapter AS Chapter,
            c.section AS Section,
            c.article AS Article,
            c.text AS Text,
            c.content_hash AS ContentHash,
            c.duplicate_of_chunk_id AS DuplicateOfChunkId,
            d.source AS Source,
            d.document_type AS DocumentType,
            d.resolution_number AS ResolutionNumber,
            d.publication_date AS PublicationDate,
            d.url AS Url,
            d.status AS Status,
            d.metadata->>'sourceFile' AS SourceFile,
            d.metadata->>'searchable' AS Searchable,
            1 - (c.embedding <=> CAST(@Embedding AS vector)) AS Score
        FROM regulation_chunks c
        JOIN regulation_documents d ON d.id = c.document_id
        WHERE c.embedding IS NOT NULL
        {FilterSql}
        ORDER BY c.embedding <=> CAST(@Embedding AS vector), c.chunk_index ASC
        LIMIT @Limit;
        """;

    private static string NormalizeSearchMode(string? searchMode)
    {
        var normalized = (searchMode ?? "full_text").Trim().Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        return normalized switch
        {
            "fts" or "fulltext" => "full_text",
            "semantic" => "semantic",
            "hybrid" => "hybrid",
            _ => "full_text"
        };
    }

    private async Task<RegulationSearchResult[]> SearchFullTextAsync(
        System.Data.Common.DbConnection connection,
        SearchRegulationRequest request,
        RegulationQueryExpansion expansion,
        int limit,
        CancellationToken cancellationToken)
    {
        var chunkMatches = new List<QuerySearchResult>();
        foreach (var searchQuery in expansion.SearchQueries)
        {
            var parameters = CreateSearchParameters(request, searchQuery, limit);
            var chunkRows = await connection.QueryAsync<SearchRow>(new CommandDefinition(
                ChunkSearchSql,
                parameters,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            chunkMatches.AddRange(chunkRows.Select(row => CreateQuerySearchResult(row, searchQuery, expansion.NormalizedQuery)));
        }

        var results = MergeSearchResults(chunkMatches, request.IncludeDuplicates, limit);
        if (results.Length > 0)
        {
            return results;
        }

        var documentMatches = new List<QuerySearchResult>();
        foreach (var searchQuery in expansion.SearchQueries)
        {
            var parameters = CreateSearchParameters(request, searchQuery, limit);
            var documentRows = await connection.QueryAsync<SearchRow>(new CommandDefinition(
                DocumentSearchSql,
                parameters,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            documentMatches.AddRange(documentRows.Select(row => CreateQuerySearchResult(row, searchQuery, expansion.NormalizedQuery)));
        }

        return MergeSearchResults(documentMatches, request.IncludeDuplicates, limit);
    }

    private async Task<RegulationSearchResult[]> SearchSemanticAsync(
        System.Data.Common.DbConnection connection,
        SearchRegulationRequest request,
        RegulationQueryExpansion expansion,
        int limit,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (embeddingGenerator is null)
        {
            warnings.Add("Semantic search requested, but no embedding generator is configured.");
            return [];
        }

        var matches = new List<QuerySearchResult>();
        foreach (var searchQuery in expansion.SearchQueries)
        {
            var embedding = await embeddingGenerator.GenerateAsync(searchQuery, cancellationToken).ConfigureAwait(false);
            var parameters = CreateSemanticSearchParameters(request, searchQuery, embedding.Vector, limit);
            var rows = await connection.QueryAsync<SearchRow>(new CommandDefinition(
                SemanticChunkSearchSql,
                parameters,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            matches.AddRange(rows.Select(row => CreateSemanticSearchResult(row, searchQuery)));
        }

        warnings.Add("Semantic search applied with pgvector exact nearest-neighbor ranking.");

        return MergeSearchResults(matches, request.IncludeDuplicates, limit);
    }

    private async Task<RegulationSearchResult[]> SearchHybridAsync(
        System.Data.Common.DbConnection connection,
        SearchRegulationRequest request,
        RegulationQueryExpansion expansion,
        int limit,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var fullText = await SearchFullTextAsync(connection, request, expansion, limit * 2, cancellationToken).ConfigureAwait(false);
        var semantic = await SearchSemanticAsync(connection, request, expansion, limit * 2, warnings, cancellationToken).ConfigureAwait(false);

        warnings.Add("Hybrid search applied. Full-text and semantic candidates were merged with source priority.");

        return MergeHybridResults(fullText, semantic, limit);
    }

    private static DynamicParameters CreateSearchParameters(
        SearchRegulationRequest request,
        string query,
        int limit)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Query", query);
        parameters.Add("Limit", Math.Max(limit * 10, 50));
        parameters.Add("Source", NormalizeFilter(request.Source));
        parameters.Add("DocumentType", NormalizeFilter(request.DocumentType));
        parameters.Add("ResolutionNumber", NormalizeFilter(request.ResolutionNumber));
        parameters.Add("Status", NormalizeFilter(request.Status));
        parameters.Add("RequiresReview", request.RequiresReview);
        parameters.Add("IncludeNonSearchable", request.IncludeNonSearchable);
        parameters.Add("AreaLike", string.IsNullOrWhiteSpace(request.Area) ? null : $"%{request.Area.Trim()}%");

        return parameters;
    }

    private static DynamicParameters CreateSemanticSearchParameters(
        SearchRegulationRequest request,
        string query,
        IReadOnlyList<float> embedding,
        int limit)
    {
        var parameters = CreateSearchParameters(request, query, limit);
        parameters.Add("Embedding", PgVectorFormatting.Format(embedding));

        return parameters;
    }

    private static RegulationSearchResult MapSearchResult(SearchRow row, string searchQuery)
    {
        var snippet = CreateSnippet(row.Text, searchQuery);
        var score = row.Score + CalculateSourcePriority(row) - (row.DuplicateOfChunkId is null ? 0 : 0.20);

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
            Score = score,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["searchMode"] = "full_text",
                ["fullTextScore"] = row.Score.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture),
                ["sourcePriorityBonus"] = CalculateSourcePriority(row).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
            },
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
        bool includeDuplicates,
        int limit) =>
        SelectDuplicateCandidates(matches, includeDuplicates)
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

    private static IReadOnlyList<QuerySearchResult> SelectDuplicateCandidates(
        IReadOnlyList<QuerySearchResult> matches,
        bool includeDuplicates)
    {
        if (includeDuplicates)
        {
            return matches;
        }

        return matches
            .GroupBy(match => string.IsNullOrWhiteSpace(match.ContentHash)
                ? match.Result.ChunkId
                : match.ContentHash, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(match => match.Result.Score)
                .ThenBy(match => match.IsDuplicate)
                .ThenBy(match => match.Result.ChunkId, StringComparer.OrdinalIgnoreCase)
                .First())
            .ToArray();
    }

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
            Metadata = result.Metadata,
            Citations = result.Citations
        };

    private static RegulationSearchResult CopyWithScoreAndMetadata(
        RegulationSearchResult result,
        double score,
        IReadOnlyDictionary<string, string> metadata) =>
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
            Metadata = metadata,
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

    private static QuerySearchResult CreateQuerySearchResult(
        SearchRow row,
        string searchQuery,
        string originalNormalizedQuery)
    {
        var result = MapSearchResult(row, searchQuery);
        var score = result.Score + CalculateOriginalQueryBonus(row.Text, searchQuery, originalNormalizedQuery);

        return new(CopyWithScore(result, score), searchQuery, row.ContentHash, row.DuplicateOfChunkId is not null);
    }

    private static QuerySearchResult CreateSemanticSearchResult(SearchRow row, string searchQuery)
    {
        var result = MapSearchResult(row, searchQuery);
        var metadata = new Dictionary<string, string>(result.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["searchMode"] = "semantic",
            ["vectorScore"] = row.Score.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
        };

        return new(CopyWithScoreAndMetadata(result, result.Score, metadata), searchQuery, row.ContentHash, row.DuplicateOfChunkId is not null);
    }

    private static RegulationSearchResult[] MergeHybridResults(
        IReadOnlyList<RegulationSearchResult> fullText,
        IReadOnlyList<RegulationSearchResult> semantic,
        int limit)
    {
        var maxFullText = fullText.Count == 0 ? 1 : Math.Max(1, fullText.Max(result => result.Score));
        var maxVector = semantic.Count == 0 ? 1 : Math.Max(1, semantic.Max(result => result.Score));
        var candidates = fullText
            .Select(result => new HybridCandidate(result, result.Score / maxFullText, 0))
            .Concat(semantic.Select(result => new HybridCandidate(result, 0, result.Score / maxVector)))
            .GroupBy(candidate => candidate.Result.ChunkId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var best = group
                    .OrderByDescending(candidate => candidate.Result.Score)
                    .First()
                    .Result;
                var fullTextScore = group.Max(candidate => candidate.FullTextScore);
                var vectorScore = group.Max(candidate => candidate.VectorScore);
                var sourcePriorityBonus = EstimateSourcePriority(best);
                var multiQueryBonus = Math.Min(0.20, (group.Count() - 1) * 0.05);
                var finalScore =
                    (0.55 * fullTextScore)
                    + (0.30 * vectorScore)
                    + (0.10 * sourcePriorityBonus)
                    + (0.05 * multiQueryBonus);
                var metadata = new Dictionary<string, string>(best.Metadata, StringComparer.OrdinalIgnoreCase)
                {
                    ["searchMode"] = "hybrid",
                    ["scoreBreakdown"] = string.Join(
                        ';',
                        $"fullTextScore={fullTextScore:0.######}",
                        $"vectorScore={vectorScore:0.######}",
                        $"sourcePriorityBonus={sourcePriorityBonus:0.######}",
                        $"multiQueryBonus={multiQueryBonus:0.######}",
                        $"finalScore={finalScore:0.######}")
                };

                return CopyWithScoreAndMetadata(best, finalScore, metadata);
            })
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.ChunkId, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();

        return candidates;
    }

    private static double EstimateSourcePriority(RegulationSearchResult result)
    {
        var text = string.Join(' ', result.DocumentId, result.Title, result.Url, result.ChunkId);
        if (result.Source.Equals("CNV", StringComparison.OrdinalIgnoreCase)
            && (text.Contains("toc2013", StringComparison.OrdinalIgnoreCase)
                || text.Contains("N.T. 2013", StringComparison.OrdinalIgnoreCase)))
        {
            return 0.25;
        }

        if (result.Source.Equals("CNV", StringComparison.OrdinalIgnoreCase))
        {
            return 0.20;
        }

        if (result.Url.Contains("texact", StringComparison.OrdinalIgnoreCase))
        {
            return 0.15;
        }

        if (result.Url.Contains("norma.htm", StringComparison.OrdinalIgnoreCase))
        {
            return 0.10;
        }

        if (result.Url.Contains("/anexos/", StringComparison.OrdinalIgnoreCase)
            || result.Url.Contains("verNorma.do", StringComparison.OrdinalIgnoreCase))
        {
            return 0.05;
        }

        return 0;
    }

    private static double CalculateSourcePriority(SearchRow row)
    {
        if (row.Searchable?.Equals("false", StringComparison.OrdinalIgnoreCase) == true)
        {
            return -0.50;
        }

        var text = string.Join(' ', row.DocumentId, row.DocumentTitle, row.SourceFile, row.Url, row.ChunkId);
        if (row.Source.Equals("CNV", StringComparison.OrdinalIgnoreCase)
            && (text.Contains("toc2013", StringComparison.OrdinalIgnoreCase)
                || text.Contains("N.T. 2013", StringComparison.OrdinalIgnoreCase)))
        {
            return 0.25;
        }

        if (row.Source.Equals("CNV", StringComparison.OrdinalIgnoreCase))
        {
            return 0.20;
        }

        if (row.Url.Contains("texact", StringComparison.OrdinalIgnoreCase)
            || (row.SourceFile?.Contains("texact", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return 0.15;
        }

        if (row.Url.Contains("norma.htm", StringComparison.OrdinalIgnoreCase)
            || (row.SourceFile?.Contains("norma", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return 0.10;
        }

        if (row.Url.Contains("/anexos/", StringComparison.OrdinalIgnoreCase)
            || row.Url.Contains("verNorma.do", StringComparison.OrdinalIgnoreCase)
            || (row.SourceFile?.Contains("anexos", StringComparison.OrdinalIgnoreCase) ?? false)
            || (row.SourceFile?.Contains("vernorma", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return 0.05;
        }

        return row.Status.Equals("candidate", StringComparison.OrdinalIgnoreCase) ? -0.10 : 0;
    }

    private static string? NormalizeFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CreateSnippet(string text, string query)
    {
        const int maxLength = 220;
        const int contextBeforeMatch = 80;

        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (text.Length <= maxLength)
        {
            return text;
        }

        var matchIndex = FindQueryMatchIndex(text, query);
        if (matchIndex < 0)
        {
            return $"{text[..maxLength]}...";
        }

        var start = Math.Max(0, matchIndex - contextBeforeMatch);
        var length = Math.Min(maxLength, text.Length - start);
        var prefix = start > 0 ? "..." : string.Empty;
        var suffix = start + length < text.Length ? "..." : string.Empty;

        return $"{prefix}{text.Substring(start, length).Trim()}{suffix}";
    }

    private static int FindQueryMatchIndex(string text, string query)
    {
        var normalizedText = StaticRegulationQueryExpander.Normalize(text);
        var queryTerms = CreateSnippetTerms(query);

        return queryTerms
            .Select(term => normalizedText.IndexOf(term, StringComparison.OrdinalIgnoreCase))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();
    }

    private static double CalculateOriginalQueryBonus(string text, string searchQuery, string originalNormalizedQuery)
    {
        if (string.IsNullOrWhiteSpace(originalNormalizedQuery))
        {
            return 0;
        }

        var normalizedText = StaticRegulationQueryExpander.Normalize(text);
        var normalizedSearchQuery = StaticRegulationQueryExpander.Normalize(searchQuery);
        var bonus = normalizedSearchQuery.Equals(originalNormalizedQuery, StringComparison.OrdinalIgnoreCase)
            ? 0.15
            : 0;

        if (normalizedText.Contains(originalNormalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            return bonus + 0.20;
        }

        var originalTokens = CreateSignificantTerms(originalNormalizedQuery);
        return originalTokens.Length > 0
            && originalTokens.All(token => normalizedText.Contains(token, StringComparison.OrdinalIgnoreCase))
                ? bonus + 0.10
                : bonus;
    }

    private static string[] CreateSignificantTerms(string query) =>
        StaticRegulationQueryExpander.Normalize(query)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length > 2)
            .Where(term => !IsStopTerm(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string[] CreateSnippetTerms(string query)
    {
        var terms = CreateSignificantTerms(query);

        return terms
            .Concat(terms.Where(term => term.StartsWith("inform", StringComparison.OrdinalIgnoreCase))
                .Select(_ => "inform"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsStopTerm(string term) =>
        term is "del" or "las" or "los" or "una" or "uno" or "para" or "con" or "por" or "de" or "la" or "el";

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

        public string? ContentHash { get; init; }

        public string? DuplicateOfChunkId { get; init; }

        public required string Source { get; init; }

        public required string DocumentType { get; init; }

        public string? ResolutionNumber { get; init; }

        public DateOnly? PublicationDate { get; init; }

        public required string Url { get; init; }

        public required string Status { get; init; }

        public string? SourceFile { get; init; }

        public string? Searchable { get; init; }

        public double Score { get; init; }
    }

    private sealed record QuerySearchResult(
        RegulationSearchResult Result,
        string SearchQuery,
        string? ContentHash,
        bool IsDuplicate);

    private sealed record HybridCandidate(
        RegulationSearchResult Result,
        double FullTextScore,
        double VectorScore);
}
