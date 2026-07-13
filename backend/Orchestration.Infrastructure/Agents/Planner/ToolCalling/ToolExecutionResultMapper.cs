using System.Text.Json;
using System.Text.Json.Nodes;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
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
            var normalizedJson = NormalizeFinancialExecutionJson(
                call.OutputJson,
                out var executionMetadataValid);
            var result = JsonSerializer.Deserialize<DataAgentResult>(
                normalizedJson,
                JsonOptions
            );

            if (result?.FinancialAnalysis is not { } financialAnalysis)
            {
                return result;
            }

            var receivedExecution = financialAnalysis.Execution;
            var execution = executionMetadataValid && receivedExecution is not null
                ? FinancialAnalysisExecution.FromStages(receivedExecution.Stages)
                : FinancialAnalysisExecution.LegacyUnknown;
            var statusContradiction = executionMetadataValid &&
                receivedExecution is not null &&
                receivedExecution.OverallStatus != execution.OverallStatus;
            var normalizedFinancialAnalysis = financialAnalysis with
            {
                Execution = execution
            };
            var requiresHumanReview = result.RequiresHumanReview ||
                statusContradiction ||
                execution.OverallStatus != FinancialAnalysisExecutionStatus.Succeeded;

            return result with
            {
                FinancialAnalysis = normalizedFinancialAnalysis,
                RequiresHumanReview = requiresHumanReview
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizeFinancialExecutionJson(
        string outputJson,
        out bool executionMetadataValid)
    {
        var root = JsonNode.Parse(outputJson);
        if (root is not JsonObject rootObject ||
            rootObject["financialAnalysis"] is not JsonObject financialAnalysis)
        {
            executionMetadataValid = true;
            return outputJson;
        }

        executionMetadataValid = IsValidExecutionMetadata(
            financialAnalysis["execution"]);
        if (executionMetadataValid)
        {
            return outputJson;
        }

        financialAnalysis["execution"] = null;
        return root.ToJsonString(JsonOptions);
    }

    private static bool IsValidExecutionMetadata(JsonNode? executionNode)
    {
        if (executionNode is not JsonObject execution ||
            !IsAggregateStatus(execution["overallStatus"]) ||
            execution["stages"] is not JsonArray stages ||
            stages.Count != FinancialAnalysisOperations.All.Count)
        {
            return false;
        }

        var operations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stageNode in stages)
        {
            if (stageNode is not JsonObject stage ||
                !TryGetString(stage["operation"], out var operation) ||
                !FinancialAnalysisOperations.All.Contains(operation, StringComparer.Ordinal) ||
                !operations.Add(operation) ||
                !TryGetString(stage["status"], out var status) ||
                !IsStageStatus(status) ||
                !TryGetNonNegativeDuration(stage["durationMilliseconds"]) ||
                !IsValidFailureCode(stage["failureCode"], status))
            {
                return false;
            }
        }

        return operations.Count == FinancialAnalysisOperations.All.Count;
    }

    private static bool IsAggregateStatus(JsonNode? statusNode)
    {
        return TryGetString(statusNode, out var status) && status is
            "legacy_unknown" or "succeeded" or "degraded" or "failed";
    }

    private static bool IsStageStatus(string status)
    {
        return status is "legacy_unknown" or "succeeded" or "failed";
    }

    private static bool TryGetNonNegativeDuration(JsonNode? durationNode)
    {
        return durationNode is JsonValue durationValue &&
            durationValue.TryGetValue<long>(out var duration) &&
            duration >= 0;
    }

    private static bool IsValidFailureCode(
        JsonNode? failureCodeNode,
        string status)
    {
        return failureCodeNode is null
            ? status != "failed"
            : TryGetNonEmptyString(failureCodeNode, out _);
    }

    private static bool TryGetNonEmptyString(
        JsonNode? node,
        out string value)
    {
        return TryGetString(node, out value) && !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetString(
        JsonNode? node,
        out string value)
    {
        if (node is JsonValue jsonValue &&
            jsonValue.TryGetValue<string>(out var text) &&
            text is not null)
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
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
