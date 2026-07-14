using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Legal.AiReview;
using Orchestration.Application.Agents.Legal.Cnv;
using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Infrastructure.Agents.Planner.ToolCalling;

namespace Orchestration.Tests.Agents.Planner.ToolCalling;

public class ToolExecutionResultMapperTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void TryMapDataResult_Should_parse_executed_data_tool_output()
    {
        var mapper = new ToolExecutionResultMapper();
        var dataResult = new DataAgentResult(
            HasAnomaly: true,
            Severity: "High",
            Summary: "Anomaly detected.",
            Engine: "Semantic Kernel + Python/CSnakes",
            Evidence:
            [
                new AnomalyEvidence(
                    Metric: "TransactionAmountZScore",
                    Value: 4.5,
                    Threshold: 3.0,
                    Interpretation: "Above threshold."
                )
            ]
        );

        var result = mapper.TryMapDataResult(
            [
                CreateExecutionResult(
                    "data.analyze_transactions",
                    JsonSerializer.Serialize(dataResult, JsonOptions)
                )
            ]
        );

        result.Should().BeEquivalentTo(dataResult);
    }

    [Fact]
    public void TryMapLegalResult_Should_round_trip_full_aggregate_result()
    {
        var mapper = new ToolExecutionResultMapper();
        var legalResult = CreateLegalResult();

        var result = mapper.TryMapLegalResult(
            [
                CreateExecutionResult(
                    "legal.search_cnv_regulation",
                    JsonSerializer.Serialize(legalResult, JsonOptions)
                )
            ]
        );

        result.Should().NotBeNull();
        result!.HasComplianceRisk.Should().BeTrue();
        result.RiskLevel.Should().Be("High");
        result.Summary.Should().Be("Full aggregate legal review.");
        result.Engine.Should().Be("Aggregate LegalAgent");
        result.Evidence.Should().BeEquivalentTo(legalResult.Evidence);
        result.Warnings.Should().Equal(legalResult.Warnings);
        result.RequiresHumanReview.Should().BeTrue();
        result.LegalReview.Should().BeEquivalentTo(legalResult.LegalReview);
        var queryStrategy = result.QueryStrategy.Should()
            .BeOfType<LegalQueryStrategyAudit>().Subject;
        queryStrategy.StrategyVersion.Should().Be("mapper_strategy_v1");
        queryStrategy.Queries.Should().HaveCount(2);
        queryStrategy.Queries[0].CitedEvidenceCount.Should().Be(2);
        queryStrategy.Queries[1].ExecutionStatus.Should().Be(
            LegalCnvQueryExecutionStatuses.Failed);

        result.Evidence.Should().BeAssignableTo<IList<LegalEvidence>>()
            .Which.IsReadOnly.Should().BeTrue();
        result.Warnings.Should().BeAssignableTo<IList<string>>()
            .Which.IsReadOnly.Should().BeTrue();
        queryStrategy.FailedStages.Should()
            .BeAssignableTo<IList<LegalDataStageFailureAudit>>()
            .Which.IsReadOnly.Should().BeTrue();
        queryStrategy.Queries.Should().BeAssignableTo<IList<LegalCnvQueryAudit>>()
            .Which.IsReadOnly.Should().BeTrue();
        queryStrategy.Queries[0].RelatedFinancialSignals.Should()
            .BeAssignableTo<IList<string>>()
            .Which.IsReadOnly.Should().BeTrue();
        result.LegalReview!.PossibleRegulatoryReviewAreas.Should()
            .BeAssignableTo<IList<PossibleRegulatoryReviewArea>>()
            .Which.IsReadOnly.Should().BeTrue();
        result.LegalReview.EvidenceReferences.Should()
            .BeAssignableTo<IList<LegalEvidenceReference>>()
            .Which.IsReadOnly.Should().BeTrue();
        result.LegalReview.Warnings.Should().BeAssignableTo<IList<string>>()
            .Which.IsReadOnly.Should().BeTrue();
        result.LegalReview.Limitations.Should().BeAssignableTo<IList<string>>()
            .Which.IsReadOnly.Should().BeTrue();
        result.LegalReview.PossibleRegulatoryReviewAreas[0]
            .RelatedFinancialSignals.Should().BeAssignableTo<IList<string>>()
            .Which.IsReadOnly.Should().BeTrue();
        result.LegalReview.PossibleRegulatoryReviewAreas[0]
            .EvidenceCitations.Should().BeAssignableTo<IList<string>>()
            .Which.IsReadOnly.Should().BeTrue();

        Action mutate = () => ((IList<string>)queryStrategy.Queries[0]
            .RelatedFinancialSignals).Add("mutated");
        mutate.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData("{ invalid-json")]
    [InlineData("null")]
    public void TryMapLegalResult_Should_return_null_for_malformed_or_null_json(
        string outputJson)
    {
        var mapper = new ToolExecutionResultMapper();

        var result = mapper.TryMapLegalResult(
            [
                CreateExecutionResult(
                    "legal.search_cnv_regulation",
                    outputJson
                )
            ]
        );

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryMapLegalResult_Should_combine_all_assessed_legal_calls_regardless_of_order(
        bool strongResultFirst)
    {
        var irrelevant = CreateAssessedLegalResult(
            relevance: "None",
            quality: "Strong",
            warning: "Irrelevant response warning.");
        var strong = CreateAssessedLegalResult(
            relevance: "Strong",
            quality: "Strong",
            warning: "Strong response warning.");
        var payloads = strongResultFirst
            ? new[] { strong, irrelevant }
            : [irrelevant, strong];

        var result = new ToolExecutionResultMapper().TryMapLegalResult(payloads
            .Select(payload => CreateExecutionResult(
                "legal.search_cnv_regulation",
                JsonSerializer.Serialize(payload, JsonOptions)))
            .ToArray());

        result.Should().NotBeNull();
        result!.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.RequiresHumanReview.Should().BeTrue();
        result.EvidenceAssessment.Should().NotBeNull();
        result.EvidenceAssessment!.Relevance.Should().Be("Strong");
        result.EvidenceAssessment.EvidenceQuality.Should().Be("Strong");
        result.EvidenceAssessment.RequiresHumanReview.Should().BeTrue();
        result.Evidence.Should().ContainSingle();
        result.Warnings.Should().Contain("Irrelevant response warning.");
        result.Warnings.Should().Contain("Strong response warning.");
        var queryStrategy = result.QueryStrategy.Should()
            .BeOfType<LegalQueryStrategyAudit>().Subject;
        queryStrategy.Queries.Should().HaveCount(4);
        queryStrategy.Queries.Select(query => query.Index).Should()
            .Equal(1, 2, 3, 4);
        queryStrategy.Queries.Should().OnlyContain(query => query.Total == 4);
    }

    [Fact]
    public void TryMapLegalResult_Should_return_null_when_any_selected_legal_payload_is_malformed()
    {
        var valid = CreateAssessedLegalResult(
            relevance: "Strong",
            quality: "Strong",
            warning: "Valid response warning.");

        var result = new ToolExecutionResultMapper().TryMapLegalResult(
        [
            CreateExecutionResult(
                "legal.search_cnv_regulation",
                JsonSerializer.Serialize(valid, JsonOptions)),
            CreateExecutionResult(
                "legal.search_cnv_regulation",
                "{ invalid-json")
        ]);

        result.Should().BeNull();
    }

    [Fact]
    public void TryMapLegalResult_Should_not_reconstruct_legacy_raw_cnv_response()
    {
        var mapper = new ToolExecutionResultMapper();
        const string rawCnvResponse = """
            {
              "query": "legacy raw query",
              "results": [
                {
                  "title": "Legacy citation",
                  "snippet": "Legacy snippet",
                  "citations": [
                    {
                      "source": "CNV",
                      "title": "Legacy citation",
                      "article": "Artículo 1"
                    }
                  ]
                }
              ],
              "warnings": []
            }
            """;

        var result = mapper.TryMapLegalResult(
            [CreateExecutionResult("legal.search_cnv_regulation", rawCnvResponse)]);

        result.Should().BeNull();
    }

    [Fact]
    public void TryMapLegalResult_Should_allow_null_optional_aggregate_sections()
    {
        var aggregate = CreateLegalResult() with
        {
            QueryStrategy = null,
            LegalReview = null
        };

        var result = new ToolExecutionResultMapper().TryMapLegalResult(
            [CreateExecutionResult(
                "legal.search_cnv_regulation",
                JsonSerializer.Serialize(aggregate, JsonOptions))]);

        result.Should().NotBeNull();
        result!.QueryStrategy.Should().BeNull();
        result.LegalReview.Should().BeNull();
    }

    [Theory]
    [InlineData("\"tampered\"")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("true")]
    public void TryMapLegalResult_Should_reject_invalid_raw_financial_analysis_status(
        string rawStatusToken)
    {
        var outputJson = LegalAggregateWithRawFinancialAnalysisStatus(
            rawStatusToken);

        var result = new ToolExecutionResultMapper().TryMapLegalResult(
            [CreateExecutionResult("legal.search_cnv_regulation", outputJson)]);

        result.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(ValidRawFinancialAnalysisStatusCases))]
    public void TryMapLegalResult_Should_accept_valid_raw_financial_analysis_status(
        string rawStatusToken,
        FinancialAnalysisExecutionStatus? expectedStatus)
    {
        var outputJson = LegalAggregateWithRawFinancialAnalysisStatus(
            rawStatusToken);

        var result = new ToolExecutionResultMapper().TryMapLegalResult(
            [CreateExecutionResult("legal.search_cnv_regulation", outputJson)]);

        result.Should().NotBeNull();
        var strategy = result!.QueryStrategy.Should()
            .BeOfType<LegalQueryStrategyAudit>().Subject;
        strategy.FinancialAnalysisStatus.Should().Be(expectedStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryMapLegalResult_Should_reject_duplicate_case_insensitive_audit_properties(
        bool duplicateTopLevelStrategy)
    {
        var root = JsonNode.Parse(
            JsonSerializer.Serialize(CreateLegalResult(), JsonOptions))!
            .AsObject();
        if (duplicateTopLevelStrategy)
        {
            root["QueryStrategy"] = JsonNode.Parse(
                root["queryStrategy"]!.ToJsonString());
        }
        else
        {
            root["queryStrategy"]!["FinancialAnalysisStatus"] = "failed";
        }

        var result = new ToolExecutionResultMapper().TryMapLegalResult(
            [CreateExecutionResult(
                "legal.search_cnv_regulation",
                root.ToJsonString(JsonOptions))]);

        result.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(InvalidLegalAggregateJsonCases))]
    public void TryMapLegalResult_Should_reject_semantically_invalid_aggregate(
        string caseName,
        string outputJson)
    {
        var result = new ToolExecutionResultMapper().TryMapLegalResult(
            [CreateExecutionResult("legal.search_cnv_regulation", outputJson)]);

        result.Should().BeNull(caseName);
    }

    [Fact]
    public void TryMapDataResult_Should_return_null_for_invalid_json()
    {
        var mapper = new ToolExecutionResultMapper();

        var result = mapper.TryMapDataResult(
            [
                CreateExecutionResult(
                    "data.analyze_transactions",
                    "{ invalid-json"
                )
            ]
        );

        result.Should().BeNull();
    }

    [Fact]
    public void TryMapDataResult_Should_preserve_explicit_human_review()
    {
        var result = MapDataResult(CreateDataResult(requiresHumanReview: true));

        result!.RequiresHumanReview.Should().BeTrue();
    }

    [Theory]
    [InlineData(FinancialAnalysisExecutionStatus.Failed)]
    [InlineData(FinancialAnalysisExecutionStatus.Degraded)]
    [InlineData(FinancialAnalysisExecutionStatus.LegacyUnknown)]
    public void TryMapDataResult_Should_require_review_for_incomplete_financial_execution(
        FinancialAnalysisExecutionStatus status)
    {
        var result = MapDataResult(CreateDataResult(status: status));

        result!.RequiresHumanReview.Should().BeTrue();
    }

    [Fact]
    public void TryMapDataResult_Should_preserve_false_review_for_succeeded_financial_execution()
    {
        var result = MapDataResult(CreateDataResult(
            status: FinancialAnalysisExecutionStatus.Succeeded,
            stages: CreateCompleteStages()));

        result!.RequiresHumanReview.Should().BeFalse();
    }

    [Fact]
    public void TryMapDataResult_Should_not_force_review_for_legacy_result_without_financial_analysis()
    {
        var result = MapDataResult(CreateDataResult());

        result!.FinancialAnalysis.Should().BeNull();
        result.RequiresHumanReview.Should().BeFalse();
    }

    [Fact]
    public void TryMapDataResult_Should_normalize_null_financial_execution_to_legacy_unknown_review()
    {
        const string outputJson = """
            {
              "hasAnomaly": false,
              "severity": "Low",
              "summary": "Partial financial result.",
              "engine": "Financial Workflow",
              "evidence": [],
              "financialAnalysis": {
                "engine": "Financial Workflow",
                "documentId": "document-1",
                "company": "Acme",
                "ratios": [],
                "comparisons": [],
                "riskSignals": [],
                "riskEvidence": [],
                "warnings": ["Preserved warning."],
                "limitations": [],
                "execution": null
              },
              "requiresHumanReview": false
            }
            """;

        var result = new ToolExecutionResultMapper().TryMapDataResult(
        [
            CreateExecutionResult("data.analyze_transactions", outputJson)
        ]);

        result.Should().NotBeNull();
        result!.RequiresHumanReview.Should().BeTrue();
        result.FinancialAnalysis.Should().NotBeNull();
        result.FinancialAnalysis!.DocumentId.Should().Be("document-1");
        result.FinancialAnalysis.Company.Should().Be("Acme");
        result.FinancialAnalysis.Warnings.Should().ContainSingle()
            .Which.Should().Be("Preserved warning.");
        result.FinancialAnalysis.Execution.Should().BeSameAs(
            FinancialAnalysisExecution.LegacyUnknown);
    }

    [Fact]
    public void TryMapDataResult_Should_canonicalize_contradictory_succeeded_signal_failure()
    {
        var executionJson = BuildExecutionJson(
            "succeeded",
            StageJson("ratios", "succeeded"),
            StageJson("comparisons", "succeeded"),
            StageJson("signals", "failed", "PYTHON_INVOCATION_FAILED"),
            StageJson("summary", "succeeded"));

        var result = MapRawDataResult(BuildRawDataJson(executionJson));

        result!.RequiresHumanReview.Should().BeTrue();
        result.FinancialAnalysis!.Execution.OverallStatus
            .Should().Be(FinancialAnalysisExecutionStatus.Failed);
        result.FinancialAnalysis.Execution.Stages.Should().HaveCount(4);
        result.FinancialAnalysis.DocumentId.Should().Be("document-1");
        result.FinancialAnalysis.Warnings.Should().Contain("Preserved warning.");
    }

    [Fact]
    public void TryMapDataResult_Should_flag_overall_contradiction_even_when_stages_derive_succeeded()
    {
        var executionJson = BuildExecutionJson(
            "failed",
            CreateSucceededStageJson());

        var result = MapRawDataResult(BuildRawDataJson(executionJson));

        result!.RequiresHumanReview.Should().BeTrue();
        result.FinancialAnalysis!.Execution.OverallStatus
            .Should().Be(FinancialAnalysisExecutionStatus.Succeeded);
    }

    [Fact]
    public void TryMapDataResult_Should_preserve_false_review_for_complete_consistent_raw_execution()
    {
        var executionJson = BuildExecutionJson(
            "succeeded",
            CreateSucceededStageJson());

        var result = MapRawDataResult(BuildRawDataJson(executionJson));

        result!.RequiresHumanReview.Should().BeFalse();
        result.FinancialAnalysis!.Execution.OverallStatus
            .Should().Be(FinancialAnalysisExecutionStatus.Succeeded);
        result.FinancialAnalysis.Execution.Stages.Select(stage => stage.Operation)
            .Should().Equal(FinancialAnalysisOperations.All);
    }

    public static TheoryData<string> InvalidExecutionMetadata => new()
    {
        BuildRawDataJson(null),
        BuildRawDataJson("null"),
        BuildRawDataJson(BuildExecutionJson("succeeded")),
        BuildRawDataJson("{\"overallStatus\":\"succeeded\"}"),
        BuildRawDataJson("{\"overallStatus\":\"succeeded\",\"stages\":null}"),
        BuildRawDataJson(BuildExecutionJson(
            "succeeded",
            StageJson("ratios", "succeeded"),
            StageJson("ratios", "succeeded"),
            StageJson("signals", "succeeded"),
            StageJson("summary", "succeeded"))),
        BuildRawDataJson(BuildExecutionJson(
            "succeeded",
            StageJson("ratios", "succeeded"),
            StageJson("comparisons", "succeeded"),
            StageJson("unknown", "succeeded"),
            StageJson("summary", "succeeded"))),
        BuildRawDataJson(BuildExecutionJson(
            "succeeded",
            StageJson("ratios", "succeeded"),
            StageJson("comparisons", "degraded"),
            StageJson("signals", "succeeded"),
            StageJson("summary", "succeeded"))),
        BuildRawDataJson(BuildExecutionJson(
            "succeeded",
            StageJson("ratios", "succeeded"),
            StageJson("comparisons", "succeeded"),
            StageJson("signals", "failed"),
            StageJson("summary", "succeeded"))),
        BuildRawDataJson(BuildExecutionJson(
            "succeeded",
            StageJson("ratios", "succeeded"),
            StageJson("comparisons", "succeeded"),
            StageJson("signals", "succeeded"),
            "null")),
        BuildRawDataJson(BuildExecutionJson(
            "succeeded",
            StageJson("ratios", "succeeded", durationMilliseconds: -1),
            StageJson("comparisons", "succeeded"),
            StageJson("signals", "succeeded"),
            StageJson("summary", "succeeded")))
    };

    [Theory]
    [MemberData(nameof(InvalidExecutionMetadata))]
    public void TryMapDataResult_Should_fail_closed_for_invalid_execution_metadata(
        string outputJson)
    {
        var result = MapRawDataResult(outputJson);

        result.Should().NotBeNull();
        result!.RequiresHumanReview.Should().BeTrue();
        result.FinancialAnalysis!.Execution.Should().BeSameAs(
            FinancialAnalysisExecution.LegacyUnknown);
        result.FinancialAnalysis.DocumentId.Should().Be("document-1");
        result.FinancialAnalysis.Warnings.Should().Contain("Preserved warning.");
    }

    [Fact]
    public void TryMapDataResult_Should_reject_raw_failure_detail_as_stage_failure_code()
    {
        const string rawFailure = "System.InvalidOperationException: sensitive adapter detail";
        var executionJson = BuildExecutionJson(
            "failed",
            StageJson("ratios", "succeeded"),
            StageJson("comparisons", "succeeded"),
            StageJson("signals", "failed", rawFailure),
            StageJson("summary", "succeeded"));

        var result = MapRawDataResult(BuildRawDataJson(executionJson));

        result!.RequiresHumanReview.Should().BeTrue();
        result.FinancialAnalysis!.Execution.Should().BeSameAs(
            FinancialAnalysisExecution.LegacyUnknown);
        result.FinancialAnalysis.Execution.Stages.Should().BeEmpty();
        result.FinancialAnalysis.DocumentId.Should().Be("document-1");
        JsonSerializer.Serialize(result, JsonOptions).Should().NotContain(rawFailure);
    }

    [Theory]
    [InlineData("succeeded")]
    [InlineData("legacy_unknown")]
    public void TryMapDataResult_Should_reject_failure_code_on_non_failed_stage(
        string stageStatus)
    {
        var executionJson = BuildExecutionJson(
            stageStatus == "succeeded" ? "succeeded" : "degraded",
            StageJson("ratios", stageStatus, "PYTHON_RESPONSE_INVALID"),
            StageJson("comparisons", "succeeded"),
            StageJson("signals", "succeeded"),
            StageJson("summary", "succeeded"));

        var result = MapRawDataResult(BuildRawDataJson(executionJson));

        result!.RequiresHumanReview.Should().BeTrue();
        result.FinancialAnalysis!.Execution.Should().BeSameAs(
            FinancialAnalysisExecution.LegacyUnknown);
        result.FinancialAnalysis.Execution.Stages.Should().BeEmpty();
        result.FinancialAnalysis.Warnings.Should().Contain("Preserved warning.");
    }

    [Theory]
    [InlineData(FinancialAnalysisFailureCodes.PythonInvocationFailed)]
    [InlineData(FinancialAnalysisFailureCodes.PythonResponseInvalid)]
    [InlineData(FinancialAnalysisFailureCodes.UnexpectedFailure)]
    public void TryMapDataResult_Should_accept_allowlisted_code_on_failed_stage(
        string failureCode)
    {
        var executionJson = BuildExecutionJson(
            "failed",
            StageJson("ratios", "succeeded"),
            StageJson("comparisons", "succeeded"),
            StageJson("signals", "failed", failureCode),
            StageJson("summary", "succeeded"));

        var result = MapRawDataResult(BuildRawDataJson(executionJson));

        result!.FinancialAnalysis!.Execution.OverallStatus
            .Should().Be(FinancialAnalysisExecutionStatus.Failed);
        result.FinancialAnalysis.Execution.Stages.Single(stage =>
            stage.Operation == FinancialAnalysisOperations.Signals)
            .FailureCode.Should().Be(failureCode);
        result.RequiresHumanReview.Should().BeTrue();
    }

    [Fact]
    public void TryMapDataResult_Should_validate_pascal_case_execution_before_deserialization()
    {
        const string rawFailure = "System.InvalidOperationException: sensitive detail";
        var executionJson = BuildExecutionJson(
            "failed",
            StageJson("ratios", "succeeded"),
            StageJson("comparisons", "succeeded"),
            StageJson("signals", "failed", rawFailure),
            StageJson("summary", "succeeded"));
        var outputJson = PascalCaseExecutionMetadata(
            BuildRawDataJson(executionJson));

        var result = MapRawDataResult(outputJson);

        result!.RequiresHumanReview.Should().BeTrue();
        result.FinancialAnalysis!.Execution.Should().BeSameAs(
            FinancialAnalysisExecution.LegacyUnknown);
        result.FinancialAnalysis.DocumentId.Should().Be("document-1");
        JsonSerializer.Serialize(result, JsonOptions).Should().NotContain(rawFailure);
    }

    [Fact]
    public void TryMapDataResult_Should_fail_closed_for_pascal_case_null_stages()
    {
        var outputJson = PascalCaseExecutionMetadata(BuildRawDataJson(
            "{\"overallStatus\":\"succeeded\",\"stages\":null}"));

        var result = MapRawDataResult(outputJson);

        result.Should().NotBeNull();
        result!.RequiresHumanReview.Should().BeTrue();
        result.FinancialAnalysis!.Execution.Should().BeSameAs(
            FinancialAnalysisExecution.LegacyUnknown);
        result.FinancialAnalysis.Warnings.Should().Contain("Preserved warning.");
    }

    [Fact]
    public void TryMapDataResult_Should_reject_case_equivalent_financial_analysis_properties()
    {
        var outputJson = BuildRawDataJsonWithDuplicateFinancialAnalysis(
            BuildExecutionJson("succeeded", CreateSucceededStageJson()));

        var result = MapRawDataResult(outputJson);

        result.Should().BeNull();
    }

    private static DataAgentResult? MapDataResult(DataAgentResult dataResult)
    {
        return new ToolExecutionResultMapper().TryMapDataResult(
        [
            CreateExecutionResult(
                "data.analyze_transactions",
                JsonSerializer.Serialize(dataResult, JsonOptions))
        ]);
    }

    private static DataAgentResult CreateDataResult(
        FinancialAnalysisExecutionStatus? status = null,
        bool requiresHumanReview = false,
        IReadOnlyList<FinancialAnalysisStageExecution>? stages = null)
    {
        FinancialAnalysisContext? financialAnalysis = null;
        if (status is not null)
        {
            financialAnalysis = new FinancialAnalysisContext(
                "Financial Workflow", "document", null, [], [], [], [], [], [])
            {
                Execution = new FinancialAnalysisExecution(status.Value, stages ?? [])
            };
        }

        return new DataAgentResult(
            false, "Low", "No anomaly.", "Test", [],
            financialAnalysis, requiresHumanReview);
    }

    private static IReadOnlyList<FinancialAnalysisStageExecution> CreateCompleteStages()
    {
        return FinancialAnalysisOperations.All
            .Select(operation => new FinancialAnalysisStageExecution(
                operation,
                FinancialAnalysisExecutionStatus.Succeeded,
                1))
            .ToArray();
    }

    private static DataAgentResult? MapRawDataResult(string outputJson)
    {
        return new ToolExecutionResultMapper().TryMapDataResult(
        [
            CreateExecutionResult("data.analyze_transactions", outputJson)
        ]);
    }

    private static string BuildRawDataJson(string? executionJson)
    {
        var executionProperty = executionJson is null
            ? string.Empty
            : $",\"execution\":{executionJson}";

        return $$"""
            {
              "hasAnomaly": false,
              "severity": "Low",
              "summary": "Partial financial result.",
              "engine": "Financial Workflow",
              "evidence": [],
              "financialAnalysis": {
                "engine": "Financial Workflow",
                "documentId": "document-1",
                "company": "Acme",
                "ratios": [],
                "comparisons": [],
                "riskSignals": [],
                "riskEvidence": [],
                "warnings": ["Preserved warning."],
                "limitations": []{{executionProperty}}
              },
              "requiresHumanReview": false
            }
            """;
    }

    private static string PascalCaseExecutionMetadata(string outputJson)
    {
        return outputJson
            .Replace("\"financialAnalysis\"", "\"FinancialAnalysis\"", StringComparison.Ordinal)
            .Replace("\"execution\"", "\"Execution\"", StringComparison.Ordinal)
            .Replace("\"overallStatus\"", "\"OverallStatus\"", StringComparison.Ordinal)
            .Replace("\"stages\"", "\"Stages\"", StringComparison.Ordinal)
            .Replace("\"operation\"", "\"Operation\"", StringComparison.Ordinal)
            .Replace("\"status\"", "\"Status\"", StringComparison.Ordinal)
            .Replace("\"durationMilliseconds\"", "\"DurationMilliseconds\"", StringComparison.Ordinal)
            .Replace("\"failureCode\"", "\"FailureCode\"", StringComparison.Ordinal);
    }

    private static string BuildRawDataJsonWithDuplicateFinancialAnalysis(
        string executionJson)
    {
        using var document = JsonDocument.Parse(BuildRawDataJson(executionJson));
        var financialAnalysisJson = document.RootElement
            .GetProperty("financialAnalysis")
            .GetRawText();

        return $$"""
            {
              "hasAnomaly": false,
              "severity": "Low",
              "summary": "Ambiguous financial result.",
              "engine": "Financial Workflow",
              "evidence": [],
              "financialAnalysis": {{financialAnalysisJson}},
              "FinancialAnalysis": {{financialAnalysisJson}},
              "requiresHumanReview": false
            }
            """;
    }

    private static string BuildExecutionJson(
        string overallStatus,
        params string[] stages)
    {
        return $$"""{"overallStatus":"{{overallStatus}}","stages":[{{string.Join(",", stages)}}]}""";
    }

    private static string[] CreateSucceededStageJson()
    {
        return FinancialAnalysisOperations.All
            .Select(operation => StageJson(operation, "succeeded"))
            .ToArray();
    }

    private static string StageJson(
        string operation,
        string status,
        string? failureCode = null,
        long durationMilliseconds = 1)
    {
        var failureCodeProperty = failureCode is null
            ? string.Empty
            : $",\"failureCode\":\"{failureCode}\"";

        return $$"""{"operation":"{{operation}}","status":"{{status}}","durationMilliseconds":{{durationMilliseconds}}{{failureCodeProperty}}}""";
    }

    public static IEnumerable<object[]> InvalidLegalAggregateJsonCases()
    {
        yield return InvalidCase("risk level", root => root["riskLevel"] = "Critical");
        yield return InvalidCase("blank summary", root => root["summary"] = " ");
        yield return InvalidCase("null evidence item", root =>
            root["evidence"] = JsonNode.Parse("[null]"));
        yield return InvalidCase("null warning item", root =>
            root["warnings"] = JsonNode.Parse("[null]"));
        yield return InvalidCase("null audit query", root =>
            root["queryStrategy"]!["queries"]![0] = null);
        yield return InvalidCase("bad query status", root =>
            root["queryStrategy"]!["queries"]![0]!["executionStatus"] = "partial");
        yield return InvalidCase("negative query count", root =>
            root["queryStrategy"]!["queries"]![0]!["resultCount"] = -1);
        yield return InvalidCase("failed query nonzero counts", root =>
            root["queryStrategy"]!["queries"]![1]!["citedEvidenceCount"] = 1);
        yield return InvalidCase("bad query index", root =>
            root["queryStrategy"]!["queries"]![0]!["index"] = 2);
        yield return InvalidCase("bad query total", root =>
            root["queryStrategy"]!["queries"]![0]!["total"] = 3);
        yield return InvalidCase("null related signal", root =>
            root["queryStrategy"]!["queries"]![0]!["relatedFinancialSignals"] =
                JsonNode.Parse("[null]"));
        yield return InvalidCase("contextual fallback reason", root =>
            root["queryStrategy"]!["fallbackReason"] =
                LegalCnvFallbackReasons.NoSpecificSignals);
        yield return InvalidCase("unknown data status", root =>
            root["queryStrategy"]!["dataToolStatus"] = "tampered");
        yield return InvalidCase("unknown failed stage operation", root =>
            root["queryStrategy"]!["failedStages"] = JsonNode.Parse(
                "[{\"operation\":\"tampered\",\"failureCode\":\"PYTHON_RESPONSE_INVALID\"}]"));
        yield return InvalidCase("null legal review warnings", root =>
            root["legalReview"]!["warnings"] = null);
        yield return InvalidCase("null legal review area", root =>
            root["legalReview"]!["possibleRegulatoryReviewAreas"] =
                JsonNode.Parse("[null]"));
        yield return InvalidCase("null legal evidence reference", root =>
            root["legalReview"]!["evidenceReferences"] = JsonNode.Parse("[null]"));
    }

    public static IEnumerable<object?[]> ValidRawFinancialAnalysisStatusCases()
    {
        yield return ["\"succeeded\"", FinancialAnalysisExecutionStatus.Succeeded];
        yield return ["\"degraded\"", FinancialAnalysisExecutionStatus.Degraded];
        yield return ["\"failed\"", FinancialAnalysisExecutionStatus.Failed];
        yield return ["\"legacy_unknown\"", FinancialAnalysisExecutionStatus.LegacyUnknown];
        yield return ["null", null];
    }

    private static string LegalAggregateWithRawFinancialAnalysisStatus(
        string rawStatusToken)
    {
        var root = JsonNode.Parse(
            JsonSerializer.Serialize(CreateLegalResult(), JsonOptions))!
            .AsObject();
        root["queryStrategy"]!["financialAnalysisStatus"] =
            JsonNode.Parse(rawStatusToken);
        return root.ToJsonString(JsonOptions);
    }

    private static object[] InvalidCase(
        string caseName,
        Action<JsonObject> mutate)
    {
        var root = JsonNode.Parse(
            JsonSerializer.Serialize(CreateLegalResult(), JsonOptions))!
            .AsObject();
        mutate(root);
        return [caseName, root.ToJsonString(JsonOptions)];
    }

    private static LegalAgentResult CreateLegalResult()
    {
        var evidenceReference = new LegalEvidenceReference(
            "CNV", "Mapper citation", "https://example.test/cnv",
            "Artículo 1", "Texto citado.", "Emisoras", 0.9);
        var queryStrategy = new LegalQueryStrategyAudit(
            "mapper_strategy_v1",
            LegalCnvQuerySources.Contextual,
            null,
            LegalDataToolStatuses.Executed,
            FinancialAnalysisExecutionStatus.Succeeded,
            [],
            [
                new LegalCnvQueryAudit(
                    1, 2, "mapper query", "Emisoras", "Mapper reason.",
                    ["signal"], LegalCnvQueryExecutionStatuses.Succeeded, 2, 2),
                new LegalCnvQueryAudit(
                    2, 2, "fallback query", null, "Fallback reason.",
                    [], LegalCnvQueryExecutionStatuses.Failed, 0, 0)
            ]
        );
        var legalReview = new LegalAnalysisReviewResult(
            "Mapper AI review.",
            [
                new PossibleRegulatoryReviewArea(
                    "Disclosure review",
                    "Review disclosure obligations.",
                    "Medium",
                    ["signal"],
                    ["Artículo 1"])
            ],
            [evidenceReference],
            ["AI warning"],
            ["AI limitation"],
            true,
            false,
            "provider",
            "model",
            null
        );

        return new LegalAgentResult(
            true,
            "High",
            "Full aggregate legal review.",
            "Aggregate LegalAgent",
            [new LegalEvidence("Mapper citation", "Artículo 1", "Texto citado.", "CNV")],
            ["Aggregate warning"],
            queryStrategy,
            legalReview,
            true
        );
    }

    private static LegalAgentResult CreateAssessedLegalResult(
        string relevance,
        string quality,
        string warning)
    {
        var relevant = relevance is "Weak" or "Strong";
        var assessment = new RegulatoryEvidenceAssessment(
            EvidenceFound: true,
            Relevance: relevance,
            Applicability: "NotEstablished",
            EvidenceQuality: quality,
            Severity: relevant ? "Warning" : "Info",
            RequiresHumanReview: relevant,
            Reasons: [$"Assessment reason: {relevance}."]);

        return CreateLegalResult() with
        {
            HasComplianceRisk = false,
            RiskLevel = "NotEstablished",
            Summary = "Retrieval-only legal result.",
            Warnings = [warning],
            LegalReview = relevant ? CreateLegalResult().LegalReview : null,
            RequiresHumanReview = relevant,
            EvidenceAssessment = assessment
        };
    }

    private static ToolExecutionResult CreateExecutionResult(
        string toolName,
        string outputJson)
    {
        return new ToolExecutionResult(
            ToolName: toolName,
            Status: ToolExecutionStatus.Executed,
            Succeeded: true,
            Summary: "Executed.",
            Engine: "Test Executor",
            OutputJson: outputJson,
            Error: null
        );
    }

}
