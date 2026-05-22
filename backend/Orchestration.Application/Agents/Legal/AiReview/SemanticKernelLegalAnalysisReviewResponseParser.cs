using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Orchestration.Application.Agents.Legal.AiReview;

public sealed class SemanticKernelLegalAnalysisReviewResponseParser
{
    public const string EmptyResponse = "empty_response";
    public const string InvalidJson = "invalid_json";
    public const string SchemaValidationFailed = "schema_validation_failed";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public SemanticKernelLegalAnalysisReviewParseResult Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Fail(EmptyResponse);
        }

        var json = ExtractJson(content);

        try
        {
            var parsed = JsonSerializer.Deserialize<LegalAnalysisReviewLlmResponse>(
                json,
                JsonOptions
            );

            if (parsed is null)
            {
                return Fail(InvalidJson);
            }

            if (string.IsNullOrWhiteSpace(parsed.ReviewSummary))
            {
                return Fail(SchemaValidationFailed);
            }

            if (!TryMapReviewAreas(parsed.PossibleRegulatoryReviewAreas, out var areas) ||
                !TryMapEvidenceReferences(parsed.EvidenceReferences, out var references))
            {
                return Fail(SchemaValidationFailed);
            }

            return new SemanticKernelLegalAnalysisReviewParseResult(
                Succeeded: true,
                Response: new SemanticKernelLegalAnalysisReviewParsedResponse(
                    ReviewSummary: parsed.ReviewSummary.Trim(),
                    PossibleRegulatoryReviewAreas: areas,
                    EvidenceReferences: references,
                    Warnings: parsed.Warnings.WhereNotBlank(),
                    Limitations: parsed.Limitations.WhereNotBlank()
                ),
                FailureReason: null
            );
        }
        catch (JsonException)
        {
            return Fail(InvalidJson);
        }
    }

    private static bool TryMapReviewAreas(
        IReadOnlyList<PossibleRegulatoryReviewAreaResponse>? rawAreas,
        out IReadOnlyList<PossibleRegulatoryReviewArea> areas)
    {
        var mapped = new List<PossibleRegulatoryReviewArea>();

        foreach (var area in rawAreas ?? [])
        {
            if (string.IsNullOrWhiteSpace(area.Title) ||
                string.IsNullOrWhiteSpace(area.Description) ||
                string.IsNullOrWhiteSpace(area.Severity))
            {
                areas = [];
                return false;
            }

            mapped.Add(new PossibleRegulatoryReviewArea(
                Title: area.Title.Trim(),
                Description: area.Description.Trim(),
                Severity: area.Severity.Trim(),
                RelatedFinancialSignals: area.RelatedFinancialSignals.WhereNotBlank(),
                EvidenceCitations: area.EvidenceCitations.WhereNotBlank()
            ));
        }

        areas = mapped;
        return true;
    }

    private static bool TryMapEvidenceReferences(
        IReadOnlyList<LegalEvidenceReferenceResponse>? rawReferences,
        out IReadOnlyList<LegalEvidenceReference> references)
    {
        var mapped = new List<LegalEvidenceReference>();

        foreach (var r in rawReferences ?? [])
        {
            if (string.IsNullOrWhiteSpace(r.Source) ||
                string.IsNullOrWhiteSpace(r.Title))
            {
                references = [];
                return false;
            }

            mapped.Add(new LegalEvidenceReference(
                Source: r.Source.Trim(),
                Title: r.Title.Trim(),
                Url: string.IsNullOrWhiteSpace(r.Url) ? null : r.Url.Trim(),
                Citation: string.IsNullOrWhiteSpace(r.Citation) ? null : r.Citation.Trim(),
                Snippet: string.IsNullOrWhiteSpace(r.Snippet) ? null : r.Snippet.Trim(),
                RegulationArea: string.IsNullOrWhiteSpace(r.RegulationArea) ? null : r.RegulationArea.Trim(),
                Score: r.Score
            ));
        }

        references = mapped;
        return true;
    }

    private static SemanticKernelLegalAnalysisReviewParseResult Fail(string reason)
    {
        return new SemanticKernelLegalAnalysisReviewParseResult(
            Succeeded: false,
            Response: null,
            FailureReason: reason
        );
    }

    private static string ExtractJson(string content)
    {
        var trimmed = content.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);

            if (firstLineEnd >= 0 && lastFence > firstLineEnd)
            {
                trimmed = trimmed[(firstLineEnd + 1)..lastFence].Trim();
            }
        }

        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            return trimmed;
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');

        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return trimmed[firstBrace..(lastBrace + 1)].Trim();
        }

        return trimmed;
    }

    private sealed record LegalAnalysisReviewLlmResponse(
        string? ReviewSummary,
        IReadOnlyList<PossibleRegulatoryReviewAreaResponse>? PossibleRegulatoryReviewAreas,
        IReadOnlyList<LegalEvidenceReferenceResponse>? EvidenceReferences,
        IReadOnlyList<string>? Warnings,
        IReadOnlyList<string>? Limitations
    );

    private sealed record PossibleRegulatoryReviewAreaResponse(
        string? Title,
        string? Description,
        string? Severity,
        IReadOnlyList<string>? RelatedFinancialSignals,
        IReadOnlyList<string>? EvidenceCitations
    );

    private sealed record LegalEvidenceReferenceResponse(
        string? Source,
        string? Title,
        string? Url,
        string? Citation,
        string? Snippet,
        string? RegulationArea,
        double? Score
    );
}

file static class SemanticKernelLegalAnalysisReviewParserExtensions
{
    public static IReadOnlyList<string> WhereNotBlank(this IReadOnlyList<string>? values)
    {
        return values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray() ?? [];
    }
}
