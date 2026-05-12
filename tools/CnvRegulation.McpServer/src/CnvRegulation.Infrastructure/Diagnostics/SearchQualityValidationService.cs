using System.Text.Json;
using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.Search;

namespace CnvRegulation.Infrastructure.Diagnostics;

/// <summary>
/// Runs curated search-quality checks against the configured search service.
/// </summary>
public sealed class SearchQualityValidationService(
    IRegulationSearchService searchService,
    IRegulationQueryExpander queryExpander) : ISearchQualityValidationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc />
    public async Task<SearchQualityValidationReport> ValidateAsync(
        ValidateSearchQualityRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.QuerySetPath))
        {
            return CreateEmptyReport(string.Empty, "Query set path is required.");
        }

        if (!File.Exists(request.QuerySetPath))
        {
            return CreateEmptyReport(request.QuerySetPath, $"Query set file does not exist: {request.QuerySetPath}");
        }

        await using var stream = File.OpenRead(request.QuerySetPath);
        var querySet = await JsonSerializer
            .DeserializeAsync<SearchQualityQuerySet>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (querySet is null)
        {
            return CreateEmptyReport(request.QuerySetPath, $"Query set file is empty or invalid: {request.QuerySetPath}");
        }

        var limit = request.Limit <= 0 ? 5 : Math.Min(request.Limit, 25);
        var results = new List<SearchQualityValidationResult>();
        foreach (var query in querySet.Queries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ValidateQueryAsync(query, limit, cancellationToken).ConfigureAwait(false));
        }

        return new SearchQualityValidationReport
        {
            QuerySetPath = request.QuerySetPath,
            Queries = results.Count,
            Passed = results.Count(result => result.Passed),
            Failed = results.Count(result => !result.Passed),
            Results = results,
            Warnings = []
        };
    }

    private async Task<SearchQualityValidationResult> ValidateQueryAsync(
        SearchQualityQuery query,
        int limit,
        CancellationToken cancellationToken)
    {
        var expansion = queryExpander.Expand(query.Query);
        var search = await searchService.SearchAsync(
            new SearchRegulationRequest
            {
                Query = query.Query,
                Limit = Math.Max(limit, query.MinResults)
            },
            cancellationToken).ConfigureAwait(false);
        var topResult = search.Results.FirstOrDefault();
        var citationsPresent = search.Results.Count > 0
            && search.Results.All(result => result.Citations.Count > 0);
        var failureReasons = CreateFailureReasons(query, search.Results, citationsPresent);

        return new SearchQualityValidationResult
        {
            Id = query.Id,
            Query = query.Query,
            ExpandedTerms = expansion.ExpandedTerms,
            ExpandedQueries = expansion.SearchQueries,
            ResultCount = search.Results.Count,
            TopResultSource = topResult?.Source,
            TopResultTitle = topResult?.Title,
            TopResultArticle = topResult?.Article,
            TopResultScore = topResult?.Score,
            CitationsPresent = citationsPresent,
            Warnings = search.Warnings,
            Passed = failureReasons.Count == 0,
            FailureReasons = failureReasons
        };
    }

    private static IReadOnlyList<string> CreateFailureReasons(
        SearchQualityQuery query,
        IReadOnlyList<RegulationSearchResult> results,
        bool citationsPresent)
    {
        var reasons = new List<string>();
        if (results.Count < Math.Max(0, query.MinResults))
        {
            reasons.Add($"expected at least {query.MinResults} result(s)");
        }

        if (results.Count > 0 && !citationsPresent)
        {
            reasons.Add("expected citations in all returned results");
        }

        if (query.ExpectedSources.Count > 0
            && results.Count > 0
            && !query.ExpectedSources.Contains(results[0].Source, StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add($"expected top source in: {string.Join(", ", query.ExpectedSources)}");
        }

        if (query.ExpectedAnyTerms.Count > 0
            && results.Count > 0
            && !ContainsAnyExpectedTerm(results, query.ExpectedAnyTerms))
        {
            reasons.Add($"expected evidence to contain any term: {string.Join(", ", query.ExpectedAnyTerms)}");
        }

        return reasons;
    }

    private static bool ContainsAnyExpectedTerm(
        IReadOnlyList<RegulationSearchResult> results,
        IReadOnlyList<string> expectedTerms)
    {
        var evidence = StaticRegulationQueryExpander.Normalize(string.Join(
            ' ',
            results.Select(result => string.Join(
                ' ',
                result.Title,
                result.Article,
                result.Snippet,
                result.Source,
                string.Join(' ', result.Citations.Select(citation => string.Join(
                    ' ',
                    citation.Title,
                    citation.Article,
                    citation.QuotedText)))))));

        return expectedTerms
            .Select(StaticRegulationQueryExpander.Normalize)
            .Where(term => term.Length > 0)
            .Any(term => evidence.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static SearchQualityValidationReport CreateEmptyReport(string querySetPath, string warning) =>
        new()
        {
            QuerySetPath = querySetPath,
            Queries = 0,
            Passed = 0,
            Failed = 0,
            Results = [],
            Warnings = [warning]
        };
}
