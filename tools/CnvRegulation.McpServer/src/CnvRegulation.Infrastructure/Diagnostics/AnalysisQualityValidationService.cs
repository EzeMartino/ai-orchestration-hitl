using System.Text.Json;
using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Infrastructure.Search;

namespace CnvRegulation.Infrastructure.Diagnostics;

/// <summary>
/// Runs curated regulatory-analysis quality checks against the configured analysis service.
/// </summary>
public sealed class AnalysisQualityValidationService(
    IComplianceAnalysisService analysisService,
    IRegulatoryTopicExtractor topicExtractor) : IAnalysisQualityValidationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc />
    public async Task<AnalysisQualityValidationReport> ValidateAsync(
        ValidateAnalysisQualityRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.CaseSetPath))
        {
            return CreateEmptyReport(string.Empty, request.UseHybridSearch, "Case set path is required.");
        }

        if (!File.Exists(request.CaseSetPath))
        {
            return CreateEmptyReport(
                request.CaseSetPath,
                request.UseHybridSearch,
                $"Case set file does not exist: {request.CaseSetPath}");
        }

        await using var stream = File.OpenRead(request.CaseSetPath);
        var caseSet = await JsonSerializer
            .DeserializeAsync<AnalysisQualityCaseSet>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (caseSet is null)
        {
            return CreateEmptyReport(
                request.CaseSetPath,
                request.UseHybridSearch,
                $"Case set file is empty or invalid: {request.CaseSetPath}");
        }

        var results = new List<AnalysisQualityValidationResult>();
        foreach (var testCase in caseSet.Cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ValidateCaseAsync(testCase, request, cancellationToken).ConfigureAwait(false));
        }

        return new AnalysisQualityValidationReport
        {
            CaseSetPath = request.CaseSetPath,
            UseHybridSearch = request.UseHybridSearch,
            Cases = results.Count,
            Passed = results.Count(result => result.Passed),
            Failed = results.Count(result => !result.Passed),
            Results = results,
            Warnings = []
        };
    }

    private async Task<AnalysisQualityValidationResult> ValidateCaseAsync(
        AnalysisQualityCase testCase,
        ValidateAnalysisQualityRequest request,
        CancellationToken cancellationToken)
    {
        var topics = topicExtractor.ExtractTopics(testCase.Text, testCase.RegulationArea);
        var response = await analysisService.AnalyzeAsync(
            new AnalyzeTextAgainstCnvRequest
            {
                Text = testCase.Text,
                RegulationArea = testCase.RegulationArea,
                StrictMode = request.StrictMode,
                UseHybridSearch = request.UseHybridSearch
            },
            cancellationToken).ConfigureAwait(false);
        var detectedTopics = topics.Select(topic => topic.Name).ToArray();
        var citationsCount = response.Findings.Count(finding =>
            !string.IsNullOrWhiteSpace(finding.Citation.Source)
            && !string.IsNullOrWhiteSpace(finding.Citation.Title)
            && !string.IsNullOrWhiteSpace(finding.Citation.Url)
            && !string.IsNullOrWhiteSpace(finding.Citation.QuotedText));
        var riskLevels = response.Findings
            .Select(finding => finding.RiskLevel)
            .Where(riskLevel => !string.IsNullOrWhiteSpace(riskLevel))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var failureReasons = CreateFailureReasons(testCase, response, detectedTopics, citationsCount);

        return new AnalysisQualityValidationResult
        {
            Id = testCase.Id,
            Text = testCase.Text,
            RegulationArea = testCase.RegulationArea,
            ExpectedStatus = testCase.ExpectedStatus,
            ActualStatus = response.Status,
            ExpectedTopics = testCase.ExpectedTopics,
            DetectedTopics = detectedTopics,
            FindingsCount = response.Findings.Count,
            CitationsCount = citationsCount,
            RiskLevels = riskLevels,
            Warnings = response.Warnings,
            Passed = failureReasons.Count == 0,
            FailureReasons = failureReasons
        };
    }

    private static IReadOnlyList<string> CreateFailureReasons(
        AnalysisQualityCase testCase,
        AnalyzeTextAgainstCnvResponse response,
        IReadOnlyList<string> detectedTopics,
        int citationsCount)
    {
        var reasons = new List<string>();
        if (!string.Equals(testCase.ExpectedStatus, response.Status, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add($"expected status {testCase.ExpectedStatus}, got {response.Status}");
        }

        if (response.Findings.Count < Math.Max(0, testCase.MinFindings))
        {
            reasons.Add($"expected at least {testCase.MinFindings} finding(s)");
        }

        if (citationsCount < Math.Max(0, testCase.MinCitations))
        {
            reasons.Add($"expected at least {testCase.MinCitations} citation(s)");
        }

        var missingTopics = testCase.ExpectedTopics
            .Where(expectedTopic => !ContainsTopic(detectedTopics, expectedTopic))
            .ToArray();
        if (missingTopics.Length > 0)
        {
            reasons.Add($"expected detected topic(s): {string.Join(", ", missingTopics)}");
        }

        return reasons;
    }

    private static bool ContainsTopic(IReadOnlyList<string> detectedTopics, string expectedTopic)
    {
        var normalizedExpected = StaticRegulationQueryExpander.Normalize(expectedTopic);
        if (normalizedExpected.Length == 0)
        {
            return true;
        }

        return detectedTopics
            .Select(StaticRegulationQueryExpander.Normalize)
            .Any(detectedTopic => detectedTopic.Contains(normalizedExpected, StringComparison.OrdinalIgnoreCase)
                || normalizedExpected.Contains(detectedTopic, StringComparison.OrdinalIgnoreCase));
    }

    private static AnalysisQualityValidationReport CreateEmptyReport(
        string caseSetPath,
        bool useHybridSearch,
        string warning) =>
        new()
        {
            CaseSetPath = caseSetPath,
            UseHybridSearch = useHybridSearch,
            Cases = 0,
            Passed = 0,
            Failed = 0,
            Results = [],
            Warnings = [warning]
        };
}
