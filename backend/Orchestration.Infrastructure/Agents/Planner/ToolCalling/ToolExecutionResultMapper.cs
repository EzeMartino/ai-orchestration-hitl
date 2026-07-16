using System.Text.Json;
using System.Text.Json.Nodes;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.Agents.Planner.ToolCalling.Mapping;

namespace Orchestration.Infrastructure.Agents.Planner.ToolCalling;

public sealed class ToolExecutionResultMapper : IToolExecutionResultMapper
{
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
            if (HasAmbiguousFinancialAnalysisMetadata(call.OutputJson))
            {
                return null;
            }

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
            GetPropertyIgnoreCase(rootObject, "financialAnalysis") is not
                JsonObject financialAnalysis)
        {
            executionMetadataValid = true;
            return outputJson;
        }

        executionMetadataValid = IsValidExecutionMetadata(
            GetPropertyIgnoreCase(financialAnalysis, "execution"));
        if (executionMetadataValid)
        {
            return outputJson;
        }

        SetPropertyToNullIgnoreCase(financialAnalysis, "execution");
        return root.ToJsonString(JsonOptions);
    }

    private static bool HasAmbiguousFinancialAnalysisMetadata(string outputJson)
    {
        using var document = JsonDocument.Parse(outputJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        JsonElement financialAnalysis = default;
        var financialAnalysisCount = 0;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!string.Equals(
                    property.Name,
                    "financialAnalysis",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            financialAnalysis = property.Value;
            financialAnalysisCount++;
        }

        if (financialAnalysisCount > 1)
        {
            return true;
        }

        if (financialAnalysisCount == 0 ||
            financialAnalysis.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        JsonElement execution = default;
        var executionCount = 0;
        foreach (var property in financialAnalysis.EnumerateObject())
        {
            if (!string.Equals(
                    property.Name,
                    "execution",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            execution = property.Value;
            executionCount++;
        }

        return executionCount > 1 ||
            executionCount == 1 && HasCaseEquivalentDuplicates(execution);
    }

    private static bool HasCaseEquivalentDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) ||
                    HasCaseEquivalentDuplicates(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (HasCaseEquivalentDuplicates(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsValidExecutionMetadata(JsonNode? executionNode)
    {
        if (executionNode is not JsonObject execution ||
            !IsAggregateStatus(GetPropertyIgnoreCase(execution, "overallStatus")) ||
            GetPropertyIgnoreCase(execution, "stages") is not JsonArray stages ||
            stages.Count != FinancialAnalysisOperations.All.Count)
        {
            return false;
        }

        var operations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stageNode in stages)
        {
            if (stageNode is not JsonObject stage ||
                !TryGetString(
                    GetPropertyIgnoreCase(stage, "operation"),
                    out var operation) ||
                !FinancialAnalysisOperations.All.Contains(operation, StringComparer.Ordinal) ||
                !operations.Add(operation) ||
                !TryGetString(GetPropertyIgnoreCase(stage, "status"), out var status) ||
                !IsStageStatus(status) ||
                !TryGetNonNegativeDuration(
                    GetPropertyIgnoreCase(stage, "durationMilliseconds")) ||
                !IsValidFailureCode(
                    GetPropertyIgnoreCase(stage, "failureCode"),
                    status))
            {
                return false;
            }
        }

        return operations.Count == FinancialAnalysisOperations.All.Count;
    }

    private static JsonNode? GetPropertyIgnoreCase(
        JsonObject value,
        string propertyName)
    {
        foreach (var property in value)
        {
            if (string.Equals(
                    property.Key,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static void SetPropertyToNullIgnoreCase(
        JsonObject value,
        string propertyName)
    {
        foreach (var property in value.ToArray())
        {
            if (string.Equals(
                    property.Key,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                value[property.Key] = null;
                return;
            }
        }

        value[propertyName] = null;
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
        if (status != "failed")
        {
            return failureCodeNode is null;
        }

        return TryGetString(failureCodeNode, out var failureCode) &&
            failureCode is
                FinancialAnalysisFailureCodes.PythonInvocationFailed or
                FinancialAnalysisFailureCodes.PythonResponseInvalid or
                FinancialAnalysisFailureCodes.UnexpectedFailure;
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
            var result = JsonSerializer.Deserialize<LegalAgentResult>(
                call.OutputJson,
                JsonOptions
            );

            return result is
            {
                RiskLevel: not null,
                Summary: not null,
                Engine: not null,
                Evidence: not null,
                Warnings: not null
            }
                ? result
                : null;
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

}
