using System.Text.Json;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;

public sealed class SemanticKernelDataAgentAiReviewResponseParser
{
    public const string EmptyResponse = "empty_response";
    public const string InvalidJson = "invalid_json";
    public const string SchemaValidationFailed = "schema_validation_failed";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public SemanticKernelDataAgentAiReviewParseResult Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Fail(EmptyResponse);
        }

        var json = ExtractJson(content);

        try
        {
            var parsed = JsonSerializer.Deserialize<DataAgentAiReviewLlmResponse>(
                json,
                JsonOptions
            );

            if (parsed is null)
            {
                return Fail(InvalidJson);
            }

            if (string.IsNullOrWhiteSpace(parsed.Summary) ||
                string.IsNullOrWhiteSpace(parsed.RiskInterpretation))
            {
                return Fail(SchemaValidationFailed);
            }

            if (!TryMapKeyFindings(parsed.KeyFindings, out var findings) ||
                !TryMapDataQualityNotes(parsed.DataQualityNotes, out var notes))
            {
                return Fail(SchemaValidationFailed);
            }

            return new SemanticKernelDataAgentAiReviewParseResult(
                Succeeded: true,
                Response: new SemanticKernelDataAgentAiReviewParsedResponse(
                    Summary: parsed.Summary.Trim(),
                    KeyFindings: findings,
                    RiskInterpretation: parsed.RiskInterpretation.Trim(),
                    DataQualityNotes: notes,
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

    private static bool TryMapKeyFindings(
        IReadOnlyList<DataAgentAiReviewKeyFindingResponse>? rawFindings,
        out IReadOnlyList<FinancialAnalysisAiKeyFinding> findings)
    {
        var mapped = new List<FinancialAnalysisAiKeyFinding>();

        foreach (var finding in rawFindings ?? [])
        {
            if (string.IsNullOrWhiteSpace(finding.Title) ||
                string.IsNullOrWhiteSpace(finding.Description) ||
                string.IsNullOrWhiteSpace(finding.Severity))
            {
                findings = [];
                return false;
            }

            mapped.Add(new FinancialAnalysisAiKeyFinding(
                Title: finding.Title.Trim(),
                Description: finding.Description.Trim(),
                Severity: finding.Severity.Trim(),
                RelatedMetrics: finding.RelatedMetrics.WhereNotBlank()
            ));
        }

        findings = mapped;
        return true;
    }

    private static bool TryMapDataQualityNotes(
        IReadOnlyList<DataAgentAiReviewDataQualityNoteResponse>? rawNotes,
        out IReadOnlyList<FinancialAnalysisAiDataQualityNote> notes)
    {
        var mapped = new List<FinancialAnalysisAiDataQualityNote>();

        foreach (var note in rawNotes ?? [])
        {
            if (string.IsNullOrWhiteSpace(note.Message) ||
                string.IsNullOrWhiteSpace(note.Severity))
            {
                notes = [];
                return false;
            }

            mapped.Add(new FinancialAnalysisAiDataQualityNote(
                Message: note.Message.Trim(),
                Severity: note.Severity.Trim(),
                RelatedFields: note.RelatedFields.WhereNotBlank()
            ));
        }

        notes = mapped;
        return true;
    }

    private static SemanticKernelDataAgentAiReviewParseResult Fail(string reason)
    {
        return new SemanticKernelDataAgentAiReviewParseResult(
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

    private sealed record DataAgentAiReviewLlmResponse(
        string? Summary,
        IReadOnlyList<DataAgentAiReviewKeyFindingResponse>? KeyFindings,
        string? RiskInterpretation,
        IReadOnlyList<DataAgentAiReviewDataQualityNoteResponse>? DataQualityNotes,
        IReadOnlyList<string>? Limitations
    );

    private sealed record DataAgentAiReviewKeyFindingResponse(
        string? Title,
        string? Description,
        string? Severity,
        IReadOnlyList<string>? RelatedMetrics
    );

    private sealed record DataAgentAiReviewDataQualityNoteResponse(
        string? Message,
        string? Severity,
        IReadOnlyList<string>? RelatedFields
    );
}

file static class SemanticKernelDataAgentAiReviewParserExtensions
{
    public static IReadOnlyList<string> WhereNotBlank(this IReadOnlyList<string>? values)
    {
        return values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray() ?? [];
    }
}
