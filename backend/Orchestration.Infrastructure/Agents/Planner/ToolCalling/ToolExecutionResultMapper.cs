using System.Text.Json;
using System.Text.Json.Nodes;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Legal.AiReview;
using Orchestration.Application.Agents.Legal.Cnv;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.Agents.Planner.ToolCalling.Mapping;

namespace Orchestration.Infrastructure.Agents.Planner.ToolCalling;

public sealed class ToolExecutionResultMapper : IToolExecutionResultMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> LegalRiskLevels =
        new(["Low", "Medium", "High", "Unknown"], StringComparer.Ordinal);
    private static readonly HashSet<string> LegalDataStatuses = new(
        [
            LegalDataToolStatuses.Executed,
            LegalDataToolStatuses.Failed,
            LegalDataToolStatuses.Absent,
            LegalDataToolStatuses.Unknown
        ],
        StringComparer.Ordinal);
    private static readonly HashSet<string> LegalFallbackReasons = new(
        [
            LegalCnvFallbackReasons.DataToolFailed,
            LegalCnvFallbackReasons.DataStageAbsent,
            LegalCnvFallbackReasons.FinancialAnalysisMissing,
            LegalCnvFallbackReasons.SignalsStageFailed,
            LegalCnvFallbackReasons.NoSpecificSignals,
            LegalCnvFallbackReasons.SignalsUnmapped,
            LegalCnvFallbackReasons.LegacyOrAmbiguousExecution
        ],
        StringComparer.Ordinal);
    private static readonly HashSet<string> FinancialFailureCodes = new(
        [
            FinancialAnalysisFailureCodes.PythonInvocationFailed,
            FinancialAnalysisFailureCodes.PythonResponseInvalid,
            FinancialAnalysisFailureCodes.UnexpectedFailure
        ],
        StringComparer.Ordinal);
    private static readonly HashSet<string> LegalFinancialAnalysisStatuses = new(
        ["succeeded", "degraded", "failed", "legacy_unknown"],
        StringComparer.Ordinal);

    private sealed record LegalAggregatePayload(
        bool HasComplianceRisk,
        string? RiskLevel,
        string? Summary,
        string? Engine,
        IReadOnlyList<LegalEvidence?>? Evidence,
        IReadOnlyList<string?>? Warnings,
        LegalQueryStrategyAudit? QueryStrategy = null,
        LegalAnalysisReviewResult? LegalReview = null,
        bool RequiresHumanReview = false);

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
            if (!HasValidRawLegalAuditStatus(call.OutputJson))
            {
                return null;
            }

            var payload = JsonSerializer.Deserialize<LegalAggregatePayload>(
                call.OutputJson,
                JsonOptions
            );

            return TryCreateLegalResult(payload);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static bool HasValidRawLegalAuditStatus(string outputJson)
    {
        using var document = JsonDocument.Parse(outputJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !TryGetUniquePropertyIgnoreCase(
                root,
                "queryStrategy",
                out var strategy,
                out var hasStrategy))
        {
            return false;
        }

        if (!hasStrategy || strategy.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (strategy.ValueKind != JsonValueKind.Object ||
            !TryGetUniquePropertyIgnoreCase(
                strategy,
                "financialAnalysisStatus",
                out var status,
                out var hasStatus))
        {
            return false;
        }

        if (!hasStatus || status.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        return status.ValueKind == JsonValueKind.String &&
            status.GetString() is { } statusText &&
            LegalFinancialAnalysisStatuses.Contains(statusText);
    }

    private static bool TryGetUniquePropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value,
        out bool found)
    {
        value = default;
        found = false;
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (found)
            {
                return false;
            }

            value = property.Value;
            found = true;
        }

        return true;
    }

    private static LegalAgentResult? TryCreateLegalResult(
        LegalAggregatePayload? payload)
    {
        if (payload is null ||
            payload.RiskLevel is null ||
            !LegalRiskLevels.Contains(payload.RiskLevel) ||
            string.IsNullOrWhiteSpace(payload.Summary) ||
            string.IsNullOrWhiteSpace(payload.Engine) ||
            payload.Evidence is null ||
            payload.Warnings is null ||
            payload.Evidence.Any(evidence => !IsValidLegalEvidence(evidence)) ||
            payload.Warnings.Any(warning => warning is null) ||
            !TrySnapshotQueryStrategy(payload.QueryStrategy, out var queryStrategy) ||
            !TrySnapshotLegalReview(payload.LegalReview, out var legalReview))
        {
            return null;
        }

        return new LegalAgentResult(
            payload.HasComplianceRisk,
            payload.RiskLevel,
            payload.Summary,
            payload.Engine,
            Array.AsReadOnly(payload.Evidence.Select(evidence => evidence!).ToArray()),
            Array.AsReadOnly(payload.Warnings.Select(warning => warning!).ToArray()),
            queryStrategy,
            legalReview,
            payload.RequiresHumanReview
        );
    }

    private static bool IsValidLegalEvidence(LegalEvidence? evidence) =>
        evidence is not null &&
        !string.IsNullOrWhiteSpace(evidence.Regulation) &&
        !string.IsNullOrWhiteSpace(evidence.Section) &&
        !string.IsNullOrWhiteSpace(evidence.Finding) &&
        !string.IsNullOrWhiteSpace(evidence.Source);

    private static bool TrySnapshotQueryStrategy(
        LegalQueryStrategyAudit? strategy,
        out LegalQueryStrategyAudit? snapshot)
    {
        snapshot = null;
        if (strategy is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(strategy.StrategyVersion) ||
            !IsValidQuerySource(strategy.Source, strategy.FallbackReason) ||
            !LegalDataStatuses.Contains(strategy.DataToolStatus) ||
            strategy.FinancialAnalysisStatus is { } financialStatus &&
                !Enum.IsDefined(financialStatus) ||
            strategy.FailedStages is null ||
            strategy.Queries is null ||
            strategy.Queries.Count is < 1 or > 4)
        {
            return false;
        }

        var failedOperations = new HashSet<string>(StringComparer.Ordinal);
        var failedStages = new List<LegalDataStageFailureAudit>(
            strategy.FailedStages.Count);
        foreach (var stage in strategy.FailedStages)
        {
            if (stage is null ||
                !FinancialAnalysisOperations.All.Contains(
                    stage.Operation,
                    StringComparer.Ordinal) ||
                !FinancialFailureCodes.Contains(stage.FailureCode) ||
                !failedOperations.Add(stage.Operation))
            {
                return false;
            }

            failedStages.Add(new LegalDataStageFailureAudit(
                stage.Operation,
                stage.FailureCode));
        }

        var queries = new List<LegalCnvQueryAudit>(strategy.Queries.Count);
        for (var queryIndex = 0; queryIndex < strategy.Queries.Count; queryIndex++)
        {
            var query = strategy.Queries[queryIndex];
            if (!IsValidLegalQuery(query, queryIndex + 1, strategy.Queries.Count))
            {
                return false;
            }

            queries.Add(new LegalCnvQueryAudit(
                query.Index,
                query.Total,
                query.Query,
                query.RegulationArea,
                query.Reason,
                Array.AsReadOnly(query.RelatedFinancialSignals.ToArray()),
                query.ExecutionStatus,
                query.ResultCount,
                query.CitedEvidenceCount));
        }

        snapshot = new LegalQueryStrategyAudit(
            strategy.StrategyVersion,
            strategy.Source,
            strategy.FallbackReason,
            strategy.DataToolStatus,
            strategy.FinancialAnalysisStatus,
            Array.AsReadOnly(failedStages.ToArray()),
            Array.AsReadOnly(queries.ToArray()));
        return true;
    }

    private static bool IsValidQuerySource(string source, string? fallbackReason) =>
        source switch
        {
            LegalCnvQuerySources.Contextual => fallbackReason is null,
            LegalCnvQuerySources.Fallback =>
                !string.IsNullOrWhiteSpace(fallbackReason) &&
                LegalFallbackReasons.Contains(fallbackReason),
            _ => false
        };

    private static bool IsValidLegalQuery(
        LegalCnvQueryAudit? query,
        int expectedIndex,
        int expectedTotal)
    {
        if (query is null ||
            query.Index != expectedIndex ||
            query.Total != expectedTotal ||
            string.IsNullOrWhiteSpace(query.Query) ||
            string.IsNullOrWhiteSpace(query.Reason) ||
            query.RelatedFinancialSignals is null ||
            query.RelatedFinancialSignals.Any(signal => signal is null) ||
            query.ExecutionStatus is not
                (LegalCnvQueryExecutionStatuses.Succeeded or
                    LegalCnvQueryExecutionStatuses.Failed) ||
            query.ResultCount < 0 ||
            query.CitedEvidenceCount < 0)
        {
            return false;
        }

        return query.ExecutionStatus != LegalCnvQueryExecutionStatuses.Failed ||
            query.ResultCount == 0 && query.CitedEvidenceCount == 0;
    }

    private static bool TrySnapshotLegalReview(
        LegalAnalysisReviewResult? review,
        out LegalAnalysisReviewResult? snapshot)
    {
        snapshot = null;
        if (review is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(review.ReviewSummary) ||
            review.PossibleRegulatoryReviewAreas is null ||
            review.EvidenceReferences is null ||
            !IsValidStringList(review.Warnings) ||
            !IsValidStringList(review.Limitations))
        {
            return false;
        }

        var reviewAreas = new List<PossibleRegulatoryReviewArea>(
            review.PossibleRegulatoryReviewAreas.Count);
        foreach (var area in review.PossibleRegulatoryReviewAreas)
        {
            if (area is null ||
                string.IsNullOrWhiteSpace(area.Title) ||
                string.IsNullOrWhiteSpace(area.Description) ||
                string.IsNullOrWhiteSpace(area.Severity) ||
                !IsValidStringList(area.RelatedFinancialSignals) ||
                !IsValidStringList(area.EvidenceCitations))
            {
                return false;
            }

            reviewAreas.Add(new PossibleRegulatoryReviewArea(
                area.Title,
                area.Description,
                area.Severity,
                Array.AsReadOnly(area.RelatedFinancialSignals.ToArray()),
                Array.AsReadOnly(area.EvidenceCitations.ToArray())));
        }

        var evidenceReferences = new List<LegalEvidenceReference>(
            review.EvidenceReferences.Count);
        foreach (var reference in review.EvidenceReferences)
        {
            if (reference is null ||
                string.IsNullOrWhiteSpace(reference.Source) ||
                string.IsNullOrWhiteSpace(reference.Title))
            {
                return false;
            }

            evidenceReferences.Add(new LegalEvidenceReference(
                reference.Source,
                reference.Title,
                reference.Url,
                reference.Citation,
                reference.Snippet,
                reference.RegulationArea,
                reference.Score));
        }

        snapshot = new LegalAnalysisReviewResult(
            review.ReviewSummary,
            Array.AsReadOnly(reviewAreas.ToArray()),
            Array.AsReadOnly(evidenceReferences.ToArray()),
            Array.AsReadOnly(review.Warnings.ToArray()),
            Array.AsReadOnly(review.Limitations.ToArray()),
            review.UsedLlm,
            review.UsedFallback,
            review.Provider,
            review.Model,
            review.FailureReason);
        return true;
    }

    private static bool IsValidStringList(IReadOnlyList<string>? values) =>
        values is not null && values.All(value => value is not null);

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
