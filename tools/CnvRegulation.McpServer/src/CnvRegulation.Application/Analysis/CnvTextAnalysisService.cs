using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;

namespace CnvRegulation.Application.Analysis;

/// <summary>
/// Evidence-based CNV text analysis service.
/// </summary>
public sealed class CnvTextAnalysisService(
    IRegulatoryTopicExtractor topicExtractor,
    IRegulationSearchService searchService,
    IRegulatoryFindingBuilder findingBuilder) : IComplianceAnalysisService
{
    private const string Disclaimer = "This is an automated regulatory review aid, not legal advice.";
    private const string AutomatedWarning = "Automated review only.";
    private const string SourceValidationWarning = "Sources may require legal validation.";
    private const string HybridWarning = "Hybrid search was used only as secondary evidence.";

    /// <inheritdoc />
    public async Task<AnalyzeTextAgainstCnvResponse> AnalyzeAsync(
        AnalyzeTextAgainstCnvRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new List<string>
        {
            AutomatedWarning,
            SourceValidationWarning
        };

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            warnings.Add("No text was provided for CNV analysis.");
            return CreateResponse("insufficient_evidence", [], warnings);
        }

        var topics = topicExtractor.ExtractTopics(request.Text, request.RegulationArea);
        if (topics.Count == 0)
        {
            warnings.Add("No clear CNV regulatory topic was detected.");
            return CreateResponse("insufficient_evidence", [], warnings);
        }

        try
        {
            var searchResults = await SearchEvidenceAsync(request, topics, warnings, cancellationToken).ConfigureAwait(false);
            if (searchResults.Count == 0)
            {
                warnings.Add("No cited CNV evidence was found for detected topics.");
                return CreateResponse("insufficient_evidence", [], warnings);
            }

            var findings = findingBuilder.BuildFindings(request.Text, topics, searchResults, request.StrictMode);
            return findings.Count == 0
                ? CreateResponse("insufficient_evidence", [], warnings)
                : CreateResponse("requires_review", findings, warnings);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            warnings.Add($"Analysis search failed: {exception.Message}");
            return CreateResponse("error", [], warnings);
        }
    }

    private async Task<IReadOnlyList<RegulationSearchResult>> SearchEvidenceAsync(
        AnalyzeTextAgainstCnvRequest request,
        IReadOnlyList<RegulatoryTopic> topics,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var results = new List<RegulationSearchResult>();
        var usedHybrid = false;

        foreach (var topic in topics)
        {
            var fullText = await SearchTopicAsync(topic, request, "full_text", cancellationToken).ConfigureAwait(false);
            AddWarnings(warnings, fullText.Warnings);
            results.AddRange(fullText.Results);

            if (ShouldRunHybridSecondPass(request, fullText.Results.Count))
            {
                var hybrid = await SearchTopicAsync(topic, request, "hybrid", cancellationToken).ConfigureAwait(false);
                AddWarnings(warnings, hybrid.Warnings);
                results.AddRange(hybrid.Results);
                usedHybrid = true;
            }
        }

        if (usedHybrid)
        {
            warnings.Add(HybridWarning);
        }

        return results
            .Where(result => result.Citations.Count > 0)
            .GroupBy(result => result.ChunkId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(result => IsHybrid(result)).ThenByDescending(result => result.Score).First())
            .OrderBy(result => IsHybrid(result))
            .ThenByDescending(result => result.Score)
            .ToArray();
    }

    private static bool ShouldRunHybridSecondPass(AnalyzeTextAgainstCnvRequest request, int fullTextResultCount) =>
        request.UseHybridSearch && fullTextResultCount < 3;

    private Task<SearchRegulationResponse> SearchTopicAsync(
        RegulatoryTopic topic,
        AnalyzeTextAgainstCnvRequest request,
        string searchMode,
        CancellationToken cancellationToken) =>
        searchService.SearchAsync(
            new SearchRegulationRequest
            {
                Query = topic.SearchQuery,
                Limit = 3,
                SearchMode = searchMode,
                IncludeDuplicates = false,
                IncludeNonSearchable = false
            },
            cancellationToken);

    private static void AddWarnings(List<string> warnings, IReadOnlyList<string> newWarnings)
    {
        foreach (var warning in newWarnings)
        {
            if (!string.IsNullOrWhiteSpace(warning)
                && !warnings.Contains(warning, StringComparer.OrdinalIgnoreCase))
            {
                warnings.Add(warning);
            }
        }
    }

    private static bool IsHybrid(RegulationSearchResult result) =>
        result.Metadata.GetValueOrDefault("searchMode")?.Equals("hybrid", StringComparison.OrdinalIgnoreCase) == true;

    private static AnalyzeTextAgainstCnvResponse CreateResponse(
        string status,
        IReadOnlyList<ComplianceFinding> findings,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Status = status,
            Findings = findings,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Disclaimer = Disclaimer
        };
}
