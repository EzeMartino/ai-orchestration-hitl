using System.Text.Json;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.Agents.Planner.ToolCalling.Mapping;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

namespace Orchestration.Infrastructure.Agents.Planner.ToolCalling;

public sealed class ToolExecutionResultMapper : IToolExecutionResultMapper
{
    private const string LegalEngine = "Semantic Kernel + MCP CNV Regulation Server";
    private const string HumanReviewWarning =
        "Recuperación regulatoria automatizada únicamente. Se requiere revisión legal humana antes de tomar decisiones operativas.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public DataAgentResult? TryMapDataResult(
        IReadOnlyList<ToolExecutionResult> executedCalls)
    {
        var call = FindSuccessfulExecutedCall(
            executedCalls,
            PlannerToolResultKind.DataAgent);

        if (call is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DataAgentResult>(
                call.OutputJson,
                JsonOptions
            );
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public LegalAgentResult? TryMapLegalResult(
        IReadOnlyList<ToolExecutionResult> executedCalls)
    {
        var call = FindSuccessfulExecutedCall(
            executedCalls,
            PlannerToolResultKind.LegalAgent);

        if (call is null)
        {
            return null;
        }

        try
        {
            var response = JsonSerializer.Deserialize<CnvRegulationSearchResponse>(
                call.OutputJson,
                JsonOptions
            );

            if (response is null)
            {
                return null;
            }

            var results = response.Results ?? [];
            var evidence = results
                .Where(result => result.Citations is { Count: > 0 })
                .SelectMany(MapEvidence)
                .ToList();
            var hasRisk = evidence.Count > 0;
            var warnings = (response.Warnings ?? [])
                .Append(HumanReviewWarning)
                .Distinct()
                .ToList();

            return new LegalAgentResult(
                HasComplianceRisk: hasRisk,
                RiskLevel: hasRisk ? "Medium" : "Low",
                Summary: hasRisk
                    ? "Se encontró evidencia regulatoria de la CNV para la anomalía financiera enviada. Se requiere revisión legal humana."
                    : "No se encontró evidencia regulatoria citada de la CNV.",
                Engine: LegalEngine,
                Evidence: evidence,
                Warnings: warnings
            );
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ToolExecutionResult? FindSuccessfulExecutedCall(
        IReadOnlyList<ToolExecutionResult> executedCalls,
        PlannerToolResultKind resultKind)
    {
        var toolNames = PlannerToolCatalog.All
            .Where(definition => definition.ResultKind == resultKind)
            .Select(definition => definition.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return executedCalls.FirstOrDefault(call =>
            toolNames.Contains(call.ToolName) &&
            call.Status == ToolExecutionStatus.Executed &&
            call.Succeeded
        );
    }

    private static IEnumerable<LegalEvidence> MapEvidence(
        CnvRegulationSearchResult result)
    {
        foreach (var citation in result.Citations ?? Array.Empty<CnvRegulationCitation>())
        {
            var section = citation.Article
                ?? citation.Section
                ?? citation.Chapter
                ?? result.Article
                ?? result.Section
                ?? result.Chapter
                ?? "N/A";

            var finding = !string.IsNullOrWhiteSpace(citation.QuotedText)
                ? citation.QuotedText
                : result.Snippet;

            yield return new LegalEvidence(
                Regulation: citation.Title,
                Section: section,
                Finding: finding,
                Source: BuildSource(citation)
            );
        }
    }

    private static string BuildSource(
        CnvRegulationCitation citation)
    {
        var parts = new[]
        {
            citation.Source,
            citation.DocumentType,
            citation.ResolutionNumber,
            citation.Url
        };

        return string.Join(" | ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
