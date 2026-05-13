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
            results.Add(await ValidateQueryAsync(query, limit, request.SearchMode, cancellationToken).ConfigureAwait(false));
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

    /// <inheritdoc />
    public async Task<SearchQualityComparisonReport> CompareAsync(
        CompareSearchQualityRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var modes = request.Modes
            .Where(mode => !string.IsNullOrWhiteSpace(mode))
            .Select(mode => mode.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        if (modes.Length < 2)
        {
            modes = ["full_text", "hybrid"];
        }

        var baseline = await ValidateAsync(
            new ValidateSearchQualityRequest
            {
                QuerySetPath = request.QuerySetPath,
                Limit = request.Limit,
                SearchMode = modes[0]
            },
            cancellationToken).ConfigureAwait(false);
        var candidate = await ValidateAsync(
            new ValidateSearchQualityRequest
            {
                QuerySetPath = request.QuerySetPath,
                Limit = request.Limit,
                SearchMode = modes[1]
            },
            cancellationToken).ConfigureAwait(false);

        var candidateById = candidate.Results.ToDictionary(result => result.Id, StringComparer.OrdinalIgnoreCase);
        var results = baseline.Results
            .Select(result => CreateComparisonResult(result, candidateById.GetValueOrDefault(result.Id)))
            .ToArray();

        return new SearchQualityComparisonReport
        {
            QuerySetPath = request.QuerySetPath,
            BaselineMode = modes[0],
            CandidateMode = modes[1],
            Queries = Math.Max(baseline.Queries, candidate.Queries),
            BaselinePassed = baseline.Passed,
            BaselineFailed = baseline.Failed,
            CandidatePassed = candidate.Passed,
            CandidateFailed = candidate.Failed,
            Results = results,
            Warnings = baseline.Warnings.Concat(candidate.Warnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private async Task<SearchQualityValidationResult> ValidateQueryAsync(
        SearchQualityQuery query,
        int limit,
        string searchMode,
        CancellationToken cancellationToken)
    {
        var expansion = queryExpander.Expand(query.Query);
        var search = await searchService.SearchAsync(
            new SearchRegulationRequest
            {
                Query = query.Query,
                Limit = Math.Max(limit, query.MinResults),
                SearchMode = searchMode
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
            TopResultSnippet = topResult?.Snippet,
            TopResultCitation = topResult?.Citations.FirstOrDefault(),
            TopResultMetadata = topResult?.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CitationsPresent = citationsPresent,
            Warnings = search.Warnings,
            Passed = failureReasons.Count == 0,
            FailureReasons = failureReasons
        };
    }

    private static SearchQualityComparisonResult CreateComparisonResult(
        SearchQualityValidationResult baseline,
        SearchQualityValidationResult? candidate)
    {
        if (candidate is null)
        {
            return new SearchQualityComparisonResult
            {
                Id = baseline.Id,
                Query = baseline.Query,
                BaselinePassed = baseline.Passed,
                CandidatePassed = false,
                BaselineTopResult = FormatTopResult(baseline),
                BaselineResult = baseline,
                CandidateTopResult = "(missing)",
                CandidateResult = null,
                TopResultChanged = true,
                CandidateScoreBreakdown = null,
                FailureReasons = ["candidate mode did not return a validation result"]
            };
        }

        var failureReasons = baseline.FailureReasons
            .Select(reason => $"baseline: {reason}")
            .Concat(candidate.FailureReasons.Select(reason => $"candidate: {reason}"))
            .ToArray();

        return new SearchQualityComparisonResult
        {
            Id = baseline.Id,
            Query = baseline.Query,
            BaselinePassed = baseline.Passed,
            CandidatePassed = candidate.Passed,
            BaselineTopResult = FormatTopResult(baseline),
            BaselineResult = baseline,
            CandidateTopResult = FormatTopResult(candidate),
            CandidateResult = candidate,
            TopResultChanged = !SameTopResult(baseline, candidate),
            CandidateScoreBreakdown = candidate.TopResultMetadata.GetValueOrDefault("scoreBreakdown"),
            FailureReasons = failureReasons
        };
    }

    private static bool SameTopResult(SearchQualityValidationResult first, SearchQualityValidationResult second) =>
        string.Equals(first.TopResultSource, second.TopResultSource, StringComparison.OrdinalIgnoreCase)
        && string.Equals(first.TopResultTitle, second.TopResultTitle, StringComparison.OrdinalIgnoreCase)
        && string.Equals(first.TopResultArticle, second.TopResultArticle, StringComparison.OrdinalIgnoreCase);

    private static string FormatTopResult(SearchQualityValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(result.TopResultSource))
        {
            return "(none)";
        }

        var article = string.IsNullOrWhiteSpace(result.TopResultArticle) ? "(no article)" : result.TopResultArticle;
        var score = result.TopResultScore is null
            ? "n/a"
            : result.TopResultScore.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        return $"{result.TopResultSource} | {result.TopResultTitle} | {article} | {score}";
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
            .Any(term => MatchesExpectedTerm(evidence, term));
    }

    private static bool MatchesExpectedTerm(string evidence, string expectedTerm)
    {
        if (evidence.Contains(expectedTerm, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var tokens = expectedTerm
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length > 2)
            .Where(term => !IsStopTerm(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return tokens.Length > 0
            && tokens.All(term => evidence.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsStopTerm(string term) =>
        term is "del" or "las" or "los" or "una" or "uno" or "para" or "con" or "por" or "de" or "la" or "el";

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
