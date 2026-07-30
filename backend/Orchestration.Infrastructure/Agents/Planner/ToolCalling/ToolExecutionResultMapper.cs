using System.Text.Json;
using System.Text.Json.Nodes;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Legal.AiReview;
using Orchestration.Application.Agents.Legal.Cnv;
using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.Agents.Planner.ToolCalling.Mapping;

namespace Orchestration.Infrastructure.Agents.Planner.ToolCalling;

public sealed class ToolExecutionResultMapper : IToolExecutionResultMapper
{
    private const string RetrievalReviewSummary =
        "Se recuperó evidencia regulatoria potencialmente relevante como posible área de revisión. La aplicabilidad no está determinada.";
    private const string RetrievalEvidenceSummary =
        "Se recuperó evidencia regulatoria, pero no se estableció relevancia ni aplicabilidad para una evaluación de cumplimiento.";
    private const string NoRetrievalEvidenceSummary =
        "No se recuperó evidencia regulatoria. La ausencia de resultados no constituye una evaluación de cumplimiento.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> LegalRiskLevels =
        new(["Low", "Medium", "High", "Unknown", "NotEstablished"], StringComparer.Ordinal);
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
        bool RequiresHumanReview = false,
        RegulatoryEvidenceAssessment? EvidenceAssessment = null,
        IReadOnlyList<RegulatoryEvidenceEnrichment?>? EvidenceEnrichments = null);

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
        var calls = FindSuccessfulExecutedCalls(
                executedCalls,
                PlannerToolResultKind.LegalAgent)
            .ToArray();

        if (calls.Length == 0)
        {
            return null;
        }

        try
        {
            var results = new List<LegalAgentResult>(calls.Length);
            foreach (var call in calls)
            {
                if (!HasValidRawLegalAuditStatus(call.OutputJson))
                {
                    return null;
                }

                var payload = JsonSerializer.Deserialize<LegalAggregatePayload>(
                    call.OutputJson,
                    JsonOptions);
                var result = TryCreateLegalResult(payload);
                if (result is null)
                {
                    return null;
                }

                results.Add(result);
            }

            return AggregateLegalResults(results);
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
                "evidenceEnrichments",
                out var enrichments,
                out var hasEnrichments) ||
            !TryGetUniquePropertyIgnoreCase(
                root,
                "queryStrategy",
                out var strategy,
                out var hasStrategy))
        {
            return false;
        }

        if (hasEnrichments &&
            enrichments.ValueKind != JsonValueKind.Null &&
            (enrichments.ValueKind != JsonValueKind.Array ||
             HasCaseEquivalentDuplicates(enrichments)))
        {
            return false;
        }

        if (!hasStrategy || strategy.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (strategy.ValueKind != JsonValueKind.Object ||
            HasCaseEquivalentDuplicates(strategy) ||
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
            !TrySnapshotLegalReview(payload.LegalReview, out var legalReview) ||
            !TrySnapshotEvidenceAssessment(
                payload.EvidenceAssessment,
                out var evidenceAssessment) ||
            !RegulatoryEvidenceEnrichmentMapper.TrySnapshotEnrichments(
                payload.EvidenceEnrichments,
                out var evidenceEnrichments) ||
            !RegulatoryEvidenceEnrichmentMapper.AreAligned(
                evidenceEnrichments,
                queryStrategy))
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
            payload.RequiresHumanReview,
            evidenceAssessment,
            evidenceEnrichments
        );
    }

    private static LegalAgentResult? AggregateLegalResults(
        IReadOnlyList<LegalAgentResult> results)
    {
        var assessments = results
            .Select(result => result.EvidenceAssessment)
            .Where(assessment => assessment is not null)
            .Cast<RegulatoryEvidenceAssessment>()
            .ToArray();
        if (assessments.Length > 0 && assessments.Length != results.Count)
        {
            return null;
        }

        var evidenceAssessment = assessments.Length == 0
            ? null
            : CombineEvidenceAssessments(assessments);
        var evidence = results
            .SelectMany(result => result.Evidence)
            .Distinct()
            .OrderBy(item => item.Regulation, StringComparer.Ordinal)
            .ThenBy(item => item.Section, StringComparer.Ordinal)
            .ThenBy(item => item.Finding, StringComparer.Ordinal)
            .ThenBy(item => item.Source, StringComparer.Ordinal)
            .ToArray();
        var warnings = results
            .SelectMany(result => result.Warnings)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(warning => warning, StringComparer.Ordinal)
            .ToArray();
        var engines = results
            .Select(result => result.Engine)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(engine => engine, StringComparer.Ordinal)
            .ToArray();
        var legalReview = MergeLegalReviews(results);
        var evidenceEnrichments =
            RegulatoryEvidenceEnrichmentMapper.MergeEnrichments(results);
        var queryStrategy = MergeQueryStrategies(results, evidenceEnrichments);
        if (evidenceEnrichments is not null &&
            queryStrategy?.Enrichments is null)
        {
            return null;
        }

        var requiresHumanReview = results.Any(result => result.RequiresHumanReview) ||
            evidenceAssessment?.RequiresHumanReview == true ||
            evidenceEnrichments?.Any(enrichment =>
                enrichment.Status !=
                    RegulatoryEvidenceEnrichmentStatuses.Verified) == true;

        if (evidenceAssessment is not null)
        {
            return new LegalAgentResult(
                HasComplianceRisk: false,
                RiskLevel: "NotEstablished",
                Summary: evidenceAssessment.RequiresHumanReview
                    ? RetrievalReviewSummary
                    : evidenceAssessment.EvidenceFound
                        ? RetrievalEvidenceSummary
                        : NoRetrievalEvidenceSummary,
                Engine: string.Join(" + ", engines),
                Evidence: Array.AsReadOnly(evidence),
                Warnings: Array.AsReadOnly(warnings),
                QueryStrategy: queryStrategy,
                LegalReview: legalReview,
                RequiresHumanReview: requiresHumanReview,
                EvidenceAssessment: evidenceAssessment,
                EvidenceEnrichments: evidenceEnrichments);
        }

        var highestRisk = results
            .Select(result => result.RiskLevel)
            .OrderByDescending(LegalRiskRank)
            .First();
        return new LegalAgentResult(
            HasComplianceRisk: results.Any(result => result.HasComplianceRisk),
            RiskLevel: highestRisk,
            Summary: string.Join(" ", results
                .Select(result => result.Summary)
                .Distinct(StringComparer.Ordinal)),
            Engine: string.Join(" + ", engines),
            Evidence: Array.AsReadOnly(evidence),
            Warnings: Array.AsReadOnly(warnings),
            QueryStrategy: queryStrategy,
            LegalReview: legalReview,
            RequiresHumanReview: requiresHumanReview,
            EvidenceEnrichments: evidenceEnrichments);
    }

    private static LegalAnalysisReviewResult? MergeLegalReviews(
        IReadOnlyList<LegalAgentResult> results)
    {
        var present = results
            .Where(result => result.LegalReview is not null)
            .ToArray();
        if (present.Length == 0)
        {
            return null;
        }

        var reviews = present
            .Select(result => result.LegalReview!)
            .ToArray();
        var relevantReviews = present
            .Where(result => result.EvidenceAssessment is null ||
                result.EvidenceAssessment.RequiresHumanReview)
            .Select(result => result.LegalReview!);
        var reviewAreas = relevantReviews
            .SelectMany(review => review.PossibleRegulatoryReviewAreas)
            .GroupBy(CreateReviewAreaKey, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var evidenceReferences = relevantReviews
            .SelectMany(review => review.EvidenceReferences)
            .Distinct()
            .OrderBy(CreateEvidenceReferenceKey, StringComparer.Ordinal)
            .ToArray();

        return new LegalAnalysisReviewResult(
            ReviewSummary: JoinDistinct(
                reviews.Select(review => review.ReviewSummary),
                " "),
            PossibleRegulatoryReviewAreas: Array.AsReadOnly(reviewAreas),
            EvidenceReferences: Array.AsReadOnly(evidenceReferences),
            Warnings: Array.AsReadOnly(reviews
                .SelectMany(review => review.Warnings)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(warning => warning, StringComparer.Ordinal)
                .ToArray()),
            Limitations: Array.AsReadOnly(reviews
                .SelectMany(review => review.Limitations)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(limitation => limitation, StringComparer.Ordinal)
                .ToArray()),
            UsedLlm: reviews.Any(review => review.UsedLlm),
            UsedFallback: reviews.Any(review => review.UsedFallback),
            Provider: JoinOptionalDistinct(
                reviews.Select(review => review.Provider),
                " + "),
            Model: JoinOptionalDistinct(
                reviews.Select(review => review.Model),
                " + "),
            FailureReason: JoinOptionalDistinct(
                reviews.Select(review => review.FailureReason),
                "; "));
    }

    private static string JoinDistinct(
        IEnumerable<string> values,
        string separator) =>
        string.Join(separator, values
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal));

    private static string? JoinOptionalDistinct(
        IEnumerable<string?> values,
        string separator)
    {
        var present = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return present.Length == 0
            ? null
            : string.Join(separator, present);
    }

    private static string CreateReviewAreaKey(
        PossibleRegulatoryReviewArea area) =>
        JsonSerializer.Serialize(area, JsonOptions);

    private static string CreateEvidenceReferenceKey(
        LegalEvidenceReference evidence) =>
        JsonSerializer.Serialize(evidence, JsonOptions);

    private static RegulatoryEvidenceAssessment CombineEvidenceAssessments(
        IReadOnlyList<RegulatoryEvidenceAssessment> assessments)
    {
        var relevance = MaxAssessmentLevel(
            assessments.Select(assessment => assessment.Relevance));
        var quality = MaxAssessmentLevel(
            assessments.Select(assessment => assessment.EvidenceQuality));
        var requiresHumanReview = relevance is "Weak" or "Strong";

        return new RegulatoryEvidenceAssessment(
            EvidenceFound: assessments.Any(assessment => assessment.EvidenceFound),
            Relevance: relevance,
            Applicability: "NotEstablished",
            EvidenceQuality: quality,
            Severity: requiresHumanReview ? "Warning" : "Info",
            RequiresHumanReview: requiresHumanReview,
            Reasons: Array.AsReadOnly(assessments
                .SelectMany(assessment => assessment.Reasons)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(reason => reason, StringComparer.Ordinal)
                .ToArray()));
    }

    private static string MaxAssessmentLevel(IEnumerable<string> levels)
    {
        var values = levels.ToArray();
        if (values.Contains("Strong", StringComparer.Ordinal)) return "Strong";
        if (values.Contains("Weak", StringComparer.Ordinal)) return "Weak";
        return "None";
    }

    private static int LegalRiskRank(string riskLevel) => riskLevel switch
    {
        "High" => 4,
        "Medium" => 3,
        "Unknown" => 2,
        "Low" => 1,
        _ => 0
    };

    private static LegalQueryStrategyAudit? MergeQueryStrategies(
        IReadOnlyList<LegalAgentResult> results,
        IReadOnlyList<RegulatoryEvidenceEnrichment>? mergedEnrichments)
    {
        var strategies = results
            .Select(result => result.QueryStrategy as LegalQueryStrategyAudit)
            .ToArray();
        if (strategies.Any(strategy => strategy is null))
        {
            return null;
        }

        var present = strategies
            .Cast<LegalQueryStrategyAudit>()
            .OrderBy(
                strategy => JsonSerializer.Serialize(strategy, JsonOptions),
                StringComparer.Ordinal)
            .ToArray();
        var queryOffset = 0;
        var strategyContexts = new List<(LegalQueryStrategyAudit Strategy, int QueryOffset)>(
            present.Length);
        foreach (var strategy in present)
        {
            strategyContexts.Add((strategy, queryOffset));
            queryOffset += strategy.Queries.Count;
        }

        var queries = present.SelectMany(strategy => strategy.Queries).ToArray();
        var total = queries.Length;
        var mergedQueries = queries.Select((query, index) => new LegalCnvQueryAudit(
            Index: index + 1,
            Total: total,
            Query: query.Query,
            RegulationArea: query.RegulationArea,
            Reason: query.Reason,
            RelatedFinancialSignals: Array.AsReadOnly(
                query.RelatedFinancialSignals.ToArray()),
            ExecutionStatus: query.ExecutionStatus,
            ResultCount: query.ResultCount,
            CitedEvidenceCount: query.CitedEvidenceCount)).ToArray();
        var allContextual = present.All(strategy =>
            strategy.Source == LegalCnvQuerySources.Contextual);
        var dataStatuses = present.Select(strategy => strategy.DataToolStatus)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var financialStatuses = present
            .Select(strategy => strategy.FinancialAnalysisStatus)
            .Distinct()
            .ToArray();

        return new LegalQueryStrategyAudit(
            StrategyVersion: string.Join("+", present
                .Select(strategy => strategy.StrategyVersion)
                .Distinct(StringComparer.Ordinal)),
            Source: allContextual
                ? LegalCnvQuerySources.Contextual
                : LegalCnvQuerySources.Fallback,
            FallbackReason: allContextual
                ? null
                : present.Select(strategy => strategy.FallbackReason)
                    .FirstOrDefault(reason => reason is not null) ??
                    LegalCnvFallbackReasons.LegacyOrAmbiguousExecution,
            DataToolStatus: dataStatuses.Length == 1
                ? dataStatuses[0]
                : LegalDataToolStatuses.Unknown,
            FinancialAnalysisStatus: financialStatuses.Length == 1
                ? financialStatuses[0]
                : null,
            FailedStages: Array.AsReadOnly(present
                .SelectMany(strategy => strategy.FailedStages)
                .GroupBy(stage => stage.Operation, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => group
                    .OrderBy(
                        stage => stage.FailureCode,
                        StringComparer.Ordinal)
                    .First())
                .ToArray()),
            Queries: Array.AsReadOnly(mergedQueries),
            Enrichments: RegulatoryEvidenceEnrichmentMapper.MergeAudits(
                strategyContexts,
                mergedEnrichments,
                results));
    }

    private static bool TrySnapshotEvidenceAssessment(
        RegulatoryEvidenceAssessment? assessment,
        out RegulatoryEvidenceAssessment? snapshot)
    {
        snapshot = null;
        if (assessment is null)
        {
            return true;
        }

        var relevant = assessment.Relevance is "Weak" or "Strong";
        if (assessment.Relevance is not ("None" or "Weak" or "Strong") ||
            assessment.Applicability != "NotEstablished" ||
            assessment.EvidenceQuality is not ("None" or "Weak" or "Strong") ||
            assessment.Severity != (relevant ? "Warning" : "Info") ||
            assessment.RequiresHumanReview != relevant ||
            assessment.Reasons is null ||
            assessment.Reasons.Any(string.IsNullOrWhiteSpace) ||
            !assessment.EvidenceFound &&
                (assessment.Relevance != "None" ||
                 assessment.EvidenceQuality != "None" ||
                 assessment.RequiresHumanReview))
        {
            return false;
        }

        snapshot = assessment with
        {
            Reasons = Array.AsReadOnly(assessment.Reasons.ToArray())
        };
        return true;
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

        if (!RegulatoryEvidenceEnrichmentMapper.TrySnapshotAudits(
                strategy.Enrichments,
                strategy.Queries.Count,
                out var enrichmentAudits))
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
            Array.AsReadOnly(queries.ToArray()),
            enrichmentAudits);
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
        return FindSuccessfulExecutedCalls(executedCalls, resultKind)
            .FirstOrDefault();
    }

    private static IEnumerable<ToolExecutionResult> FindSuccessfulExecutedCalls(
        IReadOnlyList<ToolExecutionResult> executedCalls,
        PlannerToolResultKind resultKind)
    {
        var toolNames = PlannerToolCatalog.All
            .Where(definition => definition.ResultKind == resultKind)
            .Select(definition => definition.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return executedCalls.Where(call =>
            toolNames.Contains(call.ToolName) &&
            call.Status == ToolExecutionStatus.Executed &&
            call.Succeeded
        );
    }

}
