using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.InMemory;
using CnvRegulation.Infrastructure.Search;

namespace CnvRegulation.Infrastructure.Diagnostics;

/// <summary>
/// Builds diagnostics for search expansion, matching, and filtering.
/// </summary>
public sealed class ExplainSearchQueryService(
    IRegulationSearchService searchService,
    IRegulationQueryExpander queryExpander,
    IRegulationRepository documentRepository,
    IRegulationChunkRepository chunkRepository) : IExplainSearchQueryService
{
    private const int MaxDiagnosticLimit = 25;
    private const int MaxPartialMatches = 5;

    /// <inheritdoc />
    public async Task<ExplainSearchQueryReport> ExplainAsync(
        ExplainSearchQueryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var limit = request.Limit <= 0 ? 5 : Math.Min(request.Limit, MaxDiagnosticLimit);
        var query = string.IsNullOrWhiteSpace(request.Query) ? string.Empty : request.Query.Trim();
        var expansion = queryExpander.Expand(query);
        var documents = await documentRepository.ListAsync(cancellationToken).ConfigureAwait(false);
        var chunks = await chunkRepository.ListChunksAsync(cancellationToken).ConfigureAwait(false);
        var documentLookup = documents.ToDictionary(document => document.Id, StringComparer.OrdinalIgnoreCase);
        var chunkLookup = chunks.ToDictionary(chunk => chunk.Id, StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var generatedQueries = await CreateGeneratedQueryDiagnosticsAsync(
            request,
            expansion.SearchQueries.Count == 0 ? [query] : expansion.SearchQueries,
            limit,
            warnings,
            cancellationToken).ConfigureAwait(false);
        var visibility = await CreateVisibilityDiagnosticsAsync(
            request,
            limit,
            documentLookup,
            chunkLookup,
            cancellationToken).ConfigureAwait(false);

        return new ExplainSearchQueryReport
        {
            OriginalQuery = expansion.OriginalQuery,
            NormalizedQuery = expansion.NormalizedQuery,
            ExpandedTerms = expansion.ExpandedTerms,
            GeneratedQueries = generatedQueries,
            TermPresence = CreateTermPresence(expansion, documents, chunks, documentLookup),
            TopPartialMatches = CreateTopPartialMatches(request, expansion, documents, chunks, documentLookup, limit),
            HiddenDuplicateResults = visibility.HiddenDuplicateResults,
            HiddenNonSearchableResults = visibility.HiddenNonSearchableResults,
            FiltersApplied = CreateFiltersApplied(request),
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Recommendation = CreateRecommendation(generatedQueries, visibility)
        };
    }

    private async Task<IReadOnlyList<GeneratedSearchQueryDiagnostic>> CreateGeneratedQueryDiagnosticsAsync(
        ExplainSearchQueryRequest request,
        IReadOnlyList<string> searchQueries,
        int limit,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<GeneratedSearchQueryDiagnostic>();
        foreach (var searchQuery in searchQueries)
        {
            var response = await searchService.SearchAsync(
                CreateSearchRequest(
                    request,
                    searchQuery,
                    limit,
                    includeDuplicates: true,
                    includeNonSearchable: true),
                cancellationToken).ConfigureAwait(false);

            warnings.AddRange(response.Warnings);
            diagnostics.Add(new GeneratedSearchQueryDiagnostic
            {
                Query = searchQuery,
                RawResultCount = UsesMockFallback(response) ? 0 : response.Results.Count
            });
        }

        return diagnostics;
    }

    private async Task<VisibilityDiagnostics> CreateVisibilityDiagnosticsAsync(
        ExplainSearchQueryRequest request,
        int limit,
        IReadOnlyDictionary<string, RegulationDocument> documents,
        IReadOnlyDictionary<string, RegulationChunk> chunks,
        CancellationToken cancellationToken)
    {
        var visible = await searchService.SearchAsync(
            CreateSearchRequest(request, request.Query, limit, includeDuplicates: false, includeNonSearchable: false),
            cancellationToken).ConfigureAwait(false);
        var inclusive = await searchService.SearchAsync(
            CreateSearchRequest(request, request.Query, limit, includeDuplicates: true, includeNonSearchable: true),
            cancellationToken).ConfigureAwait(false);
        var visibleIds = visible.Results
            .Select(result => result.ChunkId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inclusiveChunksByHash = inclusive.Results
            .Select(result => chunks.TryGetValue(result.ChunkId, out var chunk) ? chunk : null)
            .Where(chunk => !string.IsNullOrWhiteSpace(chunk?.ContentHash))
            .GroupBy(chunk => chunk!.ContentHash!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var hiddenResults = inclusive.Results
            .Where(result => !visibleIds.Contains(result.ChunkId))
            .ToArray();
        var hiddenDuplicates = hiddenResults.Count(result =>
        {
            if (!chunks.TryGetValue(result.ChunkId, out var chunk))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(chunk.DuplicateOfChunkId)
                || (!string.IsNullOrWhiteSpace(chunk.ContentHash)
                    && inclusiveChunksByHash.TryGetValue(chunk.ContentHash, out var hashCount)
                    && hashCount > 1);
        });
        var hiddenNonSearchable = hiddenResults.Count(result =>
            documents.TryGetValue(result.DocumentId, out var document)
            && SourcePriorityScorer.IsNonSearchable(document));

        return new VisibilityDiagnostics(hiddenDuplicates, hiddenNonSearchable);
    }

    private static IReadOnlyList<SearchTermPresence> CreateTermPresence(
        CnvRegulation.Application.Search.RegulationQueryExpansion expansion,
        IReadOnlyList<RegulationDocument> documents,
        IReadOnlyList<RegulationChunk> chunks,
        IReadOnlyDictionary<string, RegulationDocument> documentLookup)
    {
        return CreateDiagnosticTerms(expansion)
            .Select(term => CreateTermPresence(term, documents, chunks, documentLookup))
            .ToArray();
    }

    private static SearchTermPresence CreateTermPresence(
        string term,
        IReadOnlyList<RegulationDocument> documents,
        IReadOnlyList<RegulationChunk> chunks,
        IReadOnlyDictionary<string, RegulationDocument> documentLookup)
    {
        var documentMatches = documents
            .Select(document => BuildDocumentText(document))
            .Select(StaticRegulationQueryExpander.Normalize)
            .ToArray();
        var chunkMatches = chunks
            .Where(chunk => documentLookup.ContainsKey(chunk.DocumentId))
            .Select(chunk => BuildChunkText(chunk, documentLookup[chunk.DocumentId]))
            .Select(StaticRegulationQueryExpander.Normalize)
            .ToArray();

        return new SearchTermPresence
        {
            Term = term,
            FoundInChunks = chunkMatches.Count(text => MatchesTerm(text, term)),
            FoundInDocuments = documentMatches.Count(text => MatchesTerm(text, term)),
            ExactPhraseFound = chunkMatches.Concat(documentMatches)
                .Any(text => text.Contains(term, StringComparison.OrdinalIgnoreCase))
        };
    }

    private static IReadOnlyList<SearchPartialMatch> CreateTopPartialMatches(
        ExplainSearchQueryRequest request,
        CnvRegulation.Application.Search.RegulationQueryExpansion expansion,
        IReadOnlyList<RegulationDocument> documents,
        IReadOnlyList<RegulationChunk> chunks,
        IReadOnlyDictionary<string, RegulationDocument> documentLookup,
        int limit)
    {
        var tokens = CreateSignificantTokens([request.Query, .. expansion.ExpandedTerms]);
        if (tokens.Count == 0)
        {
            return [];
        }

        var chunkMatches = chunks
            .Where(chunk => documentLookup.ContainsKey(chunk.DocumentId))
            .Select(chunk => new { Chunk = chunk, Document = documentLookup[chunk.DocumentId] })
            .Where(item => MatchesFilters(item.Document, request)
                && (request.IncludeNonSearchable || !SourcePriorityScorer.IsNonSearchable(item.Document)))
            .Select(item => CreatePartialMatch(item.Document, item.Chunk, tokens))
            .Where(match => match is not null)
            .Select(match => match!)
            .ToArray();
        var chunkDocumentIds = chunks
            .Select(chunk => chunk.DocumentId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var documentMatches = documents
            .Where(document => !chunkDocumentIds.Contains(document.Id))
            .Where(document => MatchesFilters(document, request)
                && (request.IncludeNonSearchable || !SourcePriorityScorer.IsNonSearchable(document)))
            .Select(document => CreatePartialMatch(document, chunk: null, tokens))
            .Where(match => match is not null)
            .Select(match => match!)
            .ToArray();

        return chunkMatches
            .Concat(documentMatches)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.ChunkId ?? match.DocumentId, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Min(limit, MaxPartialMatches))
            .ToArray();
    }

    private static SearchPartialMatch? CreatePartialMatch(
        RegulationDocument document,
        RegulationChunk? chunk,
        IReadOnlyList<string> tokens)
    {
        var searchableText = StaticRegulationQueryExpander.Normalize(
            chunk is null ? BuildDocumentText(document) : BuildChunkText(chunk, document));
        var matchedTokens = tokens
            .Where(token => searchableText.Contains(token, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (matchedTokens.Length == 0)
        {
            return null;
        }

        var text = chunk?.Text ?? document.Text;
        return new SearchPartialMatch
        {
            DocumentId = document.Id,
            ChunkId = chunk?.Id,
            Source = document.Source,
            Title = document.Title,
            Article = chunk?.Article,
            Score = matchedTokens.Length / (double)tokens.Count,
            Snippet = CreateSnippet(text)
        };
    }

    private static SearchRegulationRequest CreateSearchRequest(
        ExplainSearchQueryRequest request,
        string query,
        int limit,
        bool includeDuplicates,
        bool includeNonSearchable) =>
        new()
        {
            Query = query,
            Area = request.Area,
            Limit = limit,
            Source = request.Source,
            DocumentType = request.DocumentType,
            ResolutionNumber = request.ResolutionNumber,
            Status = request.Status,
            RequiresReview = request.RequiresReview,
            IncludeDuplicates = includeDuplicates,
            IncludeNonSearchable = includeNonSearchable
        };

    private static IReadOnlyList<string> CreateDiagnosticTerms(
        CnvRegulation.Application.Search.RegulationQueryExpansion expansion)
    {
        var terms = new List<string>
        {
            expansion.NormalizedQuery
        };
        terms.AddRange(CreateSignificantTokens([expansion.NormalizedQuery]));
        terms.AddRange(expansion.ExpandedTerms.Select(StaticRegulationQueryExpander.Normalize));
        terms.AddRange(expansion.SearchQueries.Select(StaticRegulationQueryExpander.Normalize));

        return terms
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToArray();
    }

    private static IReadOnlyList<string> CreateSignificantTokens(IEnumerable<string> values) =>
        values
            .SelectMany(value => StaticRegulationQueryExpander.Normalize(value)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(term => term.Length > 2)
            .Where(term => !IsStopTerm(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool MatchesTerm(string normalizedText, string normalizedTerm)
    {
        if (normalizedTerm.Length == 0)
        {
            return false;
        }

        if (normalizedText.Contains(normalizedTerm, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var tokens = CreateSignificantTokens([normalizedTerm]);
        return tokens.Count > 0
            && tokens.All(token => normalizedText.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesFilters(RegulationDocument document, ExplainSearchQueryRequest request)
    {
        return MatchesOptional(document.Source, request.Source)
            && MatchesOptional(document.DocumentType, request.DocumentType)
            && MatchesOptional(document.ResolutionNumber, request.ResolutionNumber)
            && MatchesOptional(document.Status, request.Status)
            && (request.RequiresReview is null || document.RequiresReview == request.RequiresReview)
            && (string.IsNullOrWhiteSpace(request.Area)
                || document.Title.Contains(request.Area, StringComparison.OrdinalIgnoreCase)
                || document.DocumentType.Contains(request.Area, StringComparison.OrdinalIgnoreCase)
                || document.Text.Contains(request.Area, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesOptional(string? actual, string? expected) =>
        string.IsNullOrWhiteSpace(expected)
        || string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string BuildDocumentText(RegulationDocument document) =>
        string.Join(' ', document.Title, document.Text, document.Source, document.DocumentType, document.ResolutionNumber);

    private static string BuildChunkText(RegulationChunk chunk, RegulationDocument document) =>
        string.Join(
            ' ',
            chunk.Text,
            chunk.Title,
            chunk.Chapter,
            chunk.Section,
            chunk.Article,
            document.Title,
            document.Source,
            document.DocumentType,
            document.ResolutionNumber);

    private static IReadOnlyList<string> CreateFiltersApplied(ExplainSearchQueryRequest request)
    {
        var filters = new List<string>();
        AddFilter(filters, "area", request.Area);
        AddFilter(filters, "source", request.Source);
        AddFilter(filters, "documentType", request.DocumentType);
        AddFilter(filters, "resolutionNumber", request.ResolutionNumber);
        AddFilter(filters, "status", request.Status);
        if (request.RequiresReview is not null)
        {
            filters.Add($"requiresReview={request.RequiresReview.Value}");
        }

        if (request.IncludeDuplicates)
        {
            filters.Add("includeDuplicates=true");
        }

        if (request.IncludeNonSearchable)
        {
            filters.Add("includeNonSearchable=true");
        }

        return filters;
    }

    private static void AddFilter(List<string> filters, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            filters.Add($"{name}={value.Trim()}");
        }
    }

    private static string CreateRecommendation(
        IReadOnlyList<GeneratedSearchQueryDiagnostic> generatedQueries,
        VisibilityDiagnostics visibility)
    {
        if (generatedQueries.Count == 0 || generatedQueries.All(query => query.RawResultCount == 0))
        {
            return "No generated query returned results; review aliases or corpus coverage.";
        }

        if (generatedQueries[0].RawResultCount == 0
            && generatedQueries.Skip(1).Any(query => query.RawResultCount > 0))
        {
            return "Alias expansion is recovering results; inspect expanded-query matches.";
        }

        if (visibility.HiddenDuplicateResults > 0 || visibility.HiddenNonSearchableResults > 0)
        {
            return "Default duplicate or wrapper filtering affects this query; use include flags for corpus audits.";
        }

        return "Query returns results; inspect top evidence and expectations.";
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

    private static bool IsStopTerm(string term) =>
        term is "del" or "las" or "los" or "una" or "uno" or "para" or "con" or "por" or "de" or "la" or "el";

    private static bool UsesMockFallback(SearchRegulationResponse response) =>
        response.Warnings.Contains(MockRegulationData.MockWarning, StringComparer.OrdinalIgnoreCase)
        && response.Results.Count > 0
        && response.Results.All(result =>
            result.ChunkId.StartsWith("mock-", StringComparison.OrdinalIgnoreCase)
            || result.Snippet.Contains("Mock regulatory snippet", StringComparison.OrdinalIgnoreCase));

    private sealed record VisibilityDiagnostics(int HiddenDuplicateResults, int HiddenNonSearchableResults);
}
