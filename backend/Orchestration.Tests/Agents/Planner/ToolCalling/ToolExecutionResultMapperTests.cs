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
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TryMapLegalResult_Should_combine_all_assessed_legal_calls_regardless_of_order(
        bool strongResultFirst,
        bool typed)
    {
        var irrelevant = CreateAssessedLegalResult(
            relevance: "None",
            quality: "Strong",
            warning: "Irrelevant response warning.",
            id: "irrelevant");
        var strong = CreateAssessedLegalResult(
            relevance: "Strong",
            quality: "Strong",
            warning: "Strong response warning.",
            id: "strong");
        var payloads = strongResultFirst
            ? new[] { strong, irrelevant }
            : [irrelevant, strong];

        var result = new ToolExecutionResultMapper().TryMapLegalResult(payloads
            .Select(payload => CreateExecutionResult(
                "legal.search_cnv_regulation",
                typed ? "{}" : JsonSerializer.Serialize(payload, JsonOptions)) with
                { TypedLegalResult = typed ? payload : null })
            .ToArray());

        result.Should().NotBeNull();
        result!.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.RequiresHumanReview.Should().BeTrue();
        result.EvidenceAssessment.Should().NotBeNull();
        result.EvidenceAssessment!.Relevance.Should().Be("Strong");
        result.EvidenceAssessment.EvidenceQuality.Should().Be("Strong");
        result.EvidenceAssessment.RequiresHumanReview.Should().BeTrue();
        result.Evidence.Select(evidence => evidence.Regulation).Should()
            .BeEquivalentTo("irrelevant regulation", "strong regulation");
        result.Warnings.Should().Contain("Irrelevant response warning.");
        result.Warnings.Should().Contain("Strong response warning.");
        result.LegalReview.Should().NotBeNull();
        result.LegalReview!.ReviewSummary.Should().Be(
            "irrelevant review summary strong review summary");
        result.LegalReview.PossibleRegulatoryReviewAreas
            .Should().ContainSingle(area => area.Title == "strong review area");
        result.LegalReview.EvidenceReferences
            .Should().ContainSingle(reference =>
                reference.Title == "strong review citation");
        result.LegalReview.Warnings.Should().Equal(
            "irrelevant review warning",
            "strong review warning");
        result.LegalReview.Limitations.Should().Equal(
            "irrelevant review limitation",
            "strong review limitation");
        result.LegalReview.UsedLlm.Should().BeTrue();
        result.LegalReview.UsedFallback.Should().BeTrue();
        result.LegalReview.Provider.Should().Be(
            "irrelevant-provider + strong-provider");
        result.LegalReview.Model.Should().Be(
            "irrelevant-model + strong-model");
        var queryStrategy = result.QueryStrategy.Should()
            .BeOfType<LegalQueryStrategyAudit>().Subject;
        queryStrategy.Queries.Should().HaveCount(4);
        queryStrategy.Queries.Select(query => query.Index).Should()
            .Equal(1, 2, 3, 4);
        queryStrategy.Queries.Should().OnlyContain(query => query.Total == 4);
    }

    [Fact]
    public void TryMapLegalResult_TypedCanonicalDocumentWithMissingMetadata_IsRejected()
    {
        var enrichment = CreateEnrichment("typed", 1, RegulatoryEvidenceEnrichmentStatuses.Verified);
        enrichment = enrichment with { Document = enrichment.Document! with { Metadata = null! } };
        var call = CreateExecutionResult("legal.search_cnv_regulation", "{}") with
        {
            TypedLegalResult = CreateEnrichedLegalResult(enrichment)
        };

        new ToolExecutionResultMapper().TryMapLegalResult([call]).Should().BeNull();
    }

    [Theory]
    [InlineData(ToolExecutionStatus.Failed, false)]
    [InlineData(ToolExecutionStatus.SkippedDisabled, true)]
    [InlineData(ToolExecutionStatus.Executed, false)]
    public void TryMapResult_TypedPayloadDoesNotOverrideExecutionGate(
        ToolExecutionStatus status,
        bool succeeded)
    {
        var dataCall = CreateExecutionResult("data.analyze_transactions", "{}") with
        {
            Status = status,
            Succeeded = succeeded,
            TypedDataResult = CreateDataResult()
        };
        var legalCall = CreateExecutionResult("legal.search_cnv_regulation", "{}") with
        {
            Status = status,
            Succeeded = succeeded,
            TypedLegalResult = CreateLegalResult()
        };
        var mapper = new ToolExecutionResultMapper();

        mapper.TryMapDataResult([dataCall]).Should().BeNull();
        mapper.TryMapLegalResult([legalCall]).Should().BeNull();
    }

    [Theory]
    [InlineData("negative_duration")]
    [InlineData("duplicate_operation")]
    [InlineData("missing_stage")]
    [InlineData("unknown_operation")]
    [InlineData("unknown_status")]
    [InlineData("degraded_stage")]
    [InlineData("null_stage")]
    [InlineData("failed_without_code")]
    [InlineData("failed_unknown_code")]
    [InlineData("succeeded_with_code")]
    [InlineData("unknown_aggregate")]
    public void TryMapDataResult_TypedMalformedExecutionRequiresReview(string invalidPart)
    {
        var stages = CreateCompleteStages().ToArray();
        var status = FinancialAnalysisExecutionStatus.Succeeded;
        switch (invalidPart)
        {
            case "negative_duration": stages[0] = stages[0] with { DurationMilliseconds = -1 }; break;
            case "duplicate_operation": stages[1] = stages[0]; break;
            case "missing_stage": stages = stages[..^1]; break;
            case "unknown_operation": stages[0] = stages[0] with { Operation = "unknown" }; break;
            case "unknown_status": stages[0] = stages[0] with { Status = (FinancialAnalysisExecutionStatus)99 }; break;
            case "degraded_stage": stages[0] = stages[0] with { Status = FinancialAnalysisExecutionStatus.Degraded }; break;
            case "null_stage": stages[0] = null!; break;
            case "failed_without_code": stages[0] = stages[0] with { Status = FinancialAnalysisExecutionStatus.Failed }; break;
            case "failed_unknown_code": stages[0] = stages[0] with { Status = FinancialAnalysisExecutionStatus.Failed, FailureCode = "unknown" }; break;
            case "succeeded_with_code": stages[0] = stages[0] with { FailureCode = FinancialAnalysisFailureCodes.PythonInvocationFailed }; break;
            case "unknown_aggregate": status = (FinancialAnalysisExecutionStatus)99; break;
        }
        var call = CreateExecutionResult("data.analyze_transactions", "{ invalid-json") with
        {
            TypedDataResult = CreateDataResult(status, stages: stages)
        };

        var result = new ToolExecutionResultMapper().TryMapDataResult([call]);

        result.Should().NotBeNull();
        result!.RequiresHumanReview.Should().BeTrue();
        result.FinancialAnalysis!.Execution.Should().BeSameAs(FinancialAnalysisExecution.LegacyUnknown);
    }

    [Fact]
    public void TryMapLegalResult_Should_aggregate_all_relevant_legal_reviews()
    {
        var weak = CreateAssessedLegalResult(
            relevance: "Weak",
            quality: "Weak",
            warning: "Weak response warning.",
            id: "weak");
        var strong = CreateAssessedLegalResult(
            relevance: "Strong",
            quality: "Strong",
            warning: "Strong response warning.",
            id: "strong");

        var result = new ToolExecutionResultMapper().TryMapLegalResult(
        [
            CreateExecutionResult(
                "legal.search_cnv_regulation",
                JsonSerializer.Serialize(weak, JsonOptions)),
            CreateExecutionResult(
                "legal.search_cnv_regulation",
                JsonSerializer.Serialize(strong, JsonOptions))
        ]);

        result.Should().NotBeNull();
        result!.LegalReview.Should().NotBeNull();
        result.LegalReview!.ReviewSummary.Should().Be(
            "strong review summary weak review summary");
        result.LegalReview.PossibleRegulatoryReviewAreas
            .Select(area => area.Title)
            .Should().Equal("strong review area", "weak review area");
        result.LegalReview.EvidenceReferences
            .Select(reference => reference.Title)
            .Should().Equal("strong review citation", "weak review citation");
        result.LegalReview.Warnings.Should()
            .Equal("strong review warning", "weak review warning");
        result.LegalReview.Limitations.Should()
            .Equal("strong review limitation", "weak review limitation");
        result.LegalReview.UsedLlm.Should().BeTrue();
        result.LegalReview.UsedFallback.Should().BeTrue();
        result.LegalReview.Provider.Should().Be("strong-provider + weak-provider");
        result.LegalReview.Model.Should().Be("strong-model + weak-model");
        result.LegalReview.FailureReason.Should().BeNull();
    }

    [Fact]
    public void TryMapLegalResult_Should_retain_all_irrelevant_evidence_without_review_areas()
    {
        var first = CreateAssessedLegalResult(
            relevance: "None",
            quality: "Strong",
            warning: "First response warning.",
            id: "first");
        var second = CreateAssessedLegalResult(
            relevance: "None",
            quality: "Weak",
            warning: "Second response warning.",
            id: "second");

        var result = new ToolExecutionResultMapper().TryMapLegalResult(
        [
            CreateExecutionResult(
                "legal.search_cnv_regulation",
                JsonSerializer.Serialize(first, JsonOptions)),
            CreateExecutionResult(
                "legal.search_cnv_regulation",
                JsonSerializer.Serialize(second, JsonOptions))
        ]);

        result.Should().NotBeNull();
        result!.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.RequiresHumanReview.Should().BeFalse();
        result.Evidence.Select(evidence => evidence.Regulation).Should()
            .BeEquivalentTo("first regulation", "second regulation");
        result.LegalReview.Should().NotBeNull();
        result.LegalReview!.ReviewSummary.Should().Be(
            "first review summary second review summary");
        result.LegalReview.PossibleRegulatoryReviewAreas.Should().BeEmpty();
        result.LegalReview.EvidenceReferences.Should().BeEmpty();
        result.LegalReview.Warnings.Should().Equal(
            "first review warning",
            "second review warning");
        result.LegalReview.Limitations.Should().Equal(
            "first review limitation",
            "second review limitation");
        result.LegalReview.UsedLlm.Should().BeFalse();
        result.LegalReview.UsedFallback.Should().BeTrue();
        result.LegalReview.Provider.Should().Be(
            "first-provider + second-provider");
        result.LegalReview.Model.Should().Be(
            "first-model + second-model");
    }

    [Fact]
    public void TryMapLegalResult_Should_return_null_when_any_selected_legal_payload_is_malformed()
    {
        var valid = CreateAssessedLegalResult(
            relevance: "Strong",
            quality: "Strong",
            warning: "Valid response warning.",
            id: "valid");

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
        result.EvidenceEnrichments.Should().BeNull();
    }

    [Fact]
    public void TryMapLegalResult_Should_snapshot_valid_enrichment_and_audit_as_immutable()
    {
        var enrichment = CreateEnrichment(
            "enrichment-valid",
            rank: 1,
            RegulatoryEvidenceEnrichmentStatuses.Verified,
            limitations: ["segunda", "primera"]);
        var aggregate = CreateEnrichedLegalResult(enrichment);

        var result = MapLegalResults(aggregate);

        result.Should().NotBeNull();
        result!.EvidenceEnrichments.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(enrichment);
        result.EvidenceEnrichments.Should()
            .BeAssignableTo<IList<RegulatoryEvidenceEnrichment>>()
            .Which.IsReadOnly.Should().BeTrue();
        result.EvidenceEnrichments![0].Limitations.Should()
            .BeAssignableTo<IList<string>>()
            .Which.IsReadOnly.Should().BeTrue();
        result.EvidenceEnrichments[0].Document!.Metadata.Should()
            .BeAssignableTo<IDictionary<string, string>>()
            .Which.IsReadOnly.Should().BeTrue();
        result.EvidenceEnrichments[0].Document!.Citations.Should()
            .BeAssignableTo<IList<RegulatoryEvidenceCitation>>()
            .Which.IsReadOnly.Should().BeTrue();

        var strategy = result.QueryStrategy.Should()
            .BeOfType<LegalQueryStrategyAudit>().Subject;
        strategy.Enrichments.Should().ContainSingle()
            .Which.EnrichmentId.Should().Be("enrichment-valid");
        strategy.Enrichments.Should()
            .BeAssignableTo<IList<LegalCnvEnrichmentAudit>>()
            .Which.IsReadOnly.Should().BeTrue();
        strategy.Enrichments![0].ContributingQueryIndices.Should()
            .BeAssignableTo<IList<int>>()
            .Which.IsReadOnly.Should().BeTrue();
        strategy.Enrichments[0].LimitationCodes.Should()
            .BeAssignableTo<IList<string>>()
            .Which.IsReadOnly.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(InvalidEvidenceEnrichmentJsonCases))]
    public void TryMapLegalResult_Should_reject_malformed_enrichment_or_audit(
        string caseName,
        string outputJson)
    {
        var result = new ToolExecutionResultMapper().TryMapLegalResult(
            [CreateExecutionResult("legal.search_cnv_regulation", outputJson)]);

        result.Should().BeNull(caseName);
    }

    [Fact]
    public void TryMapLegalResult_Should_merge_distinct_enrichments_order_independently()
    {
        var rankedSecond = CreateEnrichedLegalResult(
            CreateEnrichment(
                "enrichment-z",
                rank: 2,
                RegulatoryEvidenceEnrichmentStatuses.Partial,
                includeArticle: false),
            "second");
        var rankedFirst = CreateEnrichedLegalResult(
            CreateEnrichment(
                "enrichment-a",
                rank: 1,
                RegulatoryEvidenceEnrichmentStatuses.Verified),
            "first");

        var forward = MapLegalResults(rankedSecond, rankedFirst);
        var reverse = MapLegalResults(rankedFirst, rankedSecond);

        forward.Should().NotBeNull();
        reverse.Should().NotBeNull();
        forward!.EvidenceEnrichments.Should().BeEquivalentTo(
            reverse!.EvidenceEnrichments,
            options => options.WithStrictOrdering());
        forward.EvidenceEnrichments!.Select(item => item.EnrichmentId)
            .Should().Equal("enrichment-a", "enrichment-z");

        var forwardAudit = forward.QueryStrategy.Should()
            .BeOfType<LegalQueryStrategyAudit>().Subject;
        var reverseAudit = reverse.QueryStrategy.Should()
            .BeOfType<LegalQueryStrategyAudit>().Subject;
        forwardAudit.Enrichments.Should().BeEquivalentTo(
            reverseAudit.Enrichments,
            options => options.WithStrictOrdering());
        forwardAudit.Enrichments!.Select(item => item.EnrichmentId)
            .Should().Equal("enrichment-a", "enrichment-z");
    }

    [Fact]
    public void TryMapLegalResult_Should_merge_richer_duplicate_and_union_limitations()
    {
        var partial = CreateEnrichment(
            "enrichment-duplicate",
            rank: 2,
            RegulatoryEvidenceEnrichmentStatuses.Partial,
            includeDocument: true,
            includeArticle: false,
            limitations: ["zeta", "compartida"]);
        var verified = CreateEnrichment(
            "enrichment-duplicate",
            rank: 1,
            RegulatoryEvidenceEnrichmentStatuses.Verified,
            includeDocument: true,
            includeArticle: true,
            limitations: ["alfa", "compartida"]);

        var result = MapLegalResults(
            CreateEnrichedLegalResult(partial, "partial"),
            CreateEnrichedLegalResult(verified, "verified"));

        var merged = result!.EvidenceEnrichments.Should().ContainSingle().Subject;
        merged.Status.Should().Be(RegulatoryEvidenceEnrichmentStatuses.Verified);
        merged.Rank.Should().Be(1);
        merged.Document.Should().NotBeNull();
        merged.Article.Should().NotBeNull();
        merged.Limitations.Should().Equal("alfa", "compartida", "zeta");
        var audit = result.QueryStrategy.Should()
            .BeOfType<LegalQueryStrategyAudit>().Subject;
        audit.Enrichments.Should().ContainSingle()
            .Which.Status.Should().Be(
                RegulatoryEvidenceEnrichmentStatuses.Verified);
        MapLegalResults(result).Should().NotBeNull(
            "the merged snapshot must remain valid mapper input");
    }

    [Fact]
    public void TryMapLegalResult_Should_not_hide_explicit_duplicate_conflict()
    {
        var verified = CreateEnrichment(
            "enrichment-conflict",
            rank: 1,
            RegulatoryEvidenceEnrichmentStatuses.Verified);
        var conflict = verified with
        {
            Status = RegulatoryEvidenceEnrichmentStatuses.Conflict,
            Limitations = ["Conflicto explícito."]
        };

        var result = MapLegalResults(
            CreateEnrichedLegalResult(verified, "verified"),
            CreateEnrichedLegalResult(conflict, "conflict"));

        result!.EvidenceEnrichments.Should().ContainSingle()
            .Which.Status.Should().Be(
                RegulatoryEvidenceEnrichmentStatuses.Conflict);
        var audit = result.QueryStrategy.Should()
            .BeOfType<LegalQueryStrategyAudit>().Subject;
        audit.Enrichments.Should().ContainSingle()
            .Which.Status.Should().Be(
                RegulatoryEvidenceEnrichmentStatuses.Conflict);
    }

    [Fact]
    public void TryMapLegalResult_Should_fail_closed_to_conflict_on_stable_identity_mismatch()
    {
        var first = CreateEnrichment(
            "enrichment-identity",
            rank: 1,
            RegulatoryEvidenceEnrichmentStatuses.Verified);
        var mismatched = first with
        {
            DocumentId = "different-document",
            Document = first.Document! with { Id = "different-document" }
        };

        var firstPayload = CreateEnrichedLegalResult(first, "first") with
        {
            HasComplianceRisk = false,
            RiskLevel = "NotEstablished",
            RequiresHumanReview = false,
            EvidenceAssessment = null
        };
        var mismatchedPayload =
            CreateEnrichedLegalResult(mismatched, "mismatched") with
            {
                HasComplianceRisk = false,
                RiskLevel = "NotEstablished",
                RequiresHumanReview = false,
                EvidenceAssessment = null
            };

        var result = MapLegalResults(firstPayload, mismatchedPayload);

        var merged = result!.EvidenceEnrichments.Should().ContainSingle().Subject;
        merged.Status.Should().Be(RegulatoryEvidenceEnrichmentStatuses.Conflict);
        merged.Document.Should().NotBeNull();
        merged.Article.Should().NotBeNull();
        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.RequiresHumanReview.Should().BeTrue();
        MapLegalResults(result).Should().NotBeNull(
            "the synthesized conflict audit must remain internally consistent");
    }

    [Theory]
    [InlineData("Artículo 1 A", "Artículo 1A", true)]
    [InlineData("Artículo   1 A", "articulo 1 A", false)]
    [InlineData("Artículo 1-A", "articulo 1A", false)]
    [InlineData("Artículo 1°", "Articulo 1", true)]
    [InlineData("ARTÍCULO É", "articulo E\u0301", false)]
    [InlineData("Artículo A\u20DD", "Articulo A", false)]
    public void TryMapLegalResult_Should_use_producer_locator_normalization(
        string firstLocator,
        string secondLocator,
        bool shouldConflict)
    {
        var first = CreateArticleOnlyEnrichment(
            "enrichment-normalization",
            firstLocator);
        var second = CreateArticleOnlyEnrichment(
            "enrichment-normalization",
            secondLocator);

        var forward = MapLegalResults(
            CreateEnrichedLegalResult(first, "first"),
            CreateEnrichedLegalResult(second, "second"));
        var reverse = MapLegalResults(
            CreateEnrichedLegalResult(second, "second"),
            CreateEnrichedLegalResult(first, "first"));

        forward.Should().NotBeNull();
        reverse.Should().NotBeNull();
        forward!.EvidenceEnrichments.Should().BeEquivalentTo(
            reverse!.EvidenceEnrichments,
            options => options.WithStrictOrdering());
        var merged = forward.EvidenceEnrichments.Should().ContainSingle().Subject;
        merged.Status.Should().Be(shouldConflict
            ? RegulatoryEvidenceEnrichmentStatuses.Conflict
            : RegulatoryEvidenceEnrichmentStatuses.Partial);
        MapLegalResults(forward).Should().NotBeNull(
            "the normalized aggregate must remain valid mapper input");
    }

    [Fact]
    public void TryMapLegalResult_Should_preserve_article_snapshot_when_identity_conflicts()
    {
        var first = CreateArticleOnlyEnrichment(
            "enrichment-article-conflict",
            "Artículo 1 A");
        var second = CreateArticleOnlyEnrichment(
            "enrichment-article-conflict",
            "Artículo 1B");

        var forward = MapLegalResults(
            CreateEnrichedLegalResult(first, "first"),
            CreateEnrichedLegalResult(second, "second"));
        var reverse = MapLegalResults(
            CreateEnrichedLegalResult(second, "second"),
            CreateEnrichedLegalResult(first, "first"));

        AssertConflictAggregateIsOrderIndependentAndRemappable(forward, reverse);
        var merged = forward!.EvidenceEnrichments.Should().ContainSingle().Subject;
        merged.Document.Should().BeNull();
        merged.Article.Should().NotBeNull();
        var audit = ((LegalQueryStrategyAudit)forward.QueryStrategy!)
            .Enrichments.Should().ContainSingle().Subject;
        audit.Document.Status.Should().Be(
            LegalCnvEnrichmentStageStatuses.Missing);
        audit.Article.Status.Should().Be(
            LegalCnvEnrichmentStageStatuses.Conflict);
    }

    [Fact]
    public void TryMapLegalResult_Should_retain_valid_stages_for_snapshotless_identity_conflict()
    {
        var first = CreateUnavailableEnrichment(
            "enrichment-unavailable-conflict",
            documentId: "document-a");
        var second = CreateUnavailableEnrichment(
            "enrichment-unavailable-conflict",
            documentId: "document-b");

        var forward = MapLegalResults(
            CreateEnrichedLegalResult(first, "first"),
            CreateEnrichedLegalResult(second, "second"));
        var reverse = MapLegalResults(
            CreateEnrichedLegalResult(second, "second"),
            CreateEnrichedLegalResult(first, "first"));

        AssertConflictAggregateIsOrderIndependentAndRemappable(forward, reverse);
        var merged = forward!.EvidenceEnrichments.Should().ContainSingle().Subject;
        merged.Document.Should().BeNull();
        merged.Article.Should().BeNull();
        merged.Limitations.Should().Contain(limitation =>
            limitation.Contains("identidad", StringComparison.OrdinalIgnoreCase));
        var audit = ((LegalQueryStrategyAudit)forward.QueryStrategy!)
            .Enrichments.Should().ContainSingle().Subject;
        audit.Document.Status.Should().Be(
            LegalCnvEnrichmentStageStatuses.Missing);
        audit.Article.Status.Should().Be(
            LegalCnvEnrichmentStageStatuses.Missing);
        audit.LimitationCodes.Should().Contain(
            "aggregate_identity_conflict");
    }

    [Fact]
    public void TryMapLegalResult_Should_preserve_explicit_conflict_snapshot_over_unavailable_duplicate()
    {
        var conflict = CreateEnrichment(
            "enrichment-explicit-conflict",
            rank: 1,
            RegulatoryEvidenceEnrichmentStatuses.Conflict,
            includeDocument: true,
            includeArticle: false,
            limitations: ["Conflicto explícito."]);
        var unavailable = CreateUnavailableEnrichment(
            "enrichment-explicit-conflict",
            documentId: conflict.DocumentId);

        var forward = MapLegalResults(
            CreateEnrichedLegalResult(conflict, "conflict"),
            CreateEnrichedLegalResult(unavailable, "unavailable"));
        var reverse = MapLegalResults(
            CreateEnrichedLegalResult(unavailable, "unavailable"),
            CreateEnrichedLegalResult(conflict, "conflict"));

        AssertConflictAggregateIsOrderIndependentAndRemappable(forward, reverse);
        var merged = forward!.EvidenceEnrichments.Should().ContainSingle().Subject;
        merged.Document.Should().BeEquivalentTo(conflict.Document);
        var audit = ((LegalQueryStrategyAudit)forward.QueryStrategy!)
            .Enrichments.Should().ContainSingle().Subject;
        audit.Document.Status.Should().Be(
            LegalCnvEnrichmentStageStatuses.Conflict);
    }

    [Fact]
    public void TryMapLegalResult_Should_fail_closed_on_candidate_key_mismatch()
    {
        var enrichment = CreateEnrichment(
            "enrichment-candidate-conflict",
            rank: 1,
            RegulatoryEvidenceEnrichmentStatuses.Verified);
        var first = WithCandidateKey(
            CreateEnrichedLegalResult(enrichment, "first"),
            "candidate-a");
        var second = WithCandidateKey(
            CreateEnrichedLegalResult(enrichment, "second"),
            "candidate-b");

        var forward = MapLegalResults(first, second);
        var reverse = MapLegalResults(second, first);

        AssertConflictAggregateIsOrderIndependentAndRemappable(forward, reverse);
        var audit = ((LegalQueryStrategyAudit)forward!.QueryStrategy!)
            .Enrichments.Should().ContainSingle().Subject;
        audit.LimitationCodes.Should().Contain(
            "aggregate_candidate_key_conflict");
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

    [Fact]
    public void TryMapLegalResult_Should_reject_duplicate_case_insensitive_enrichment_properties()
    {
        var json = JsonSerializer.Serialize(
            CreateEnrichedLegalResult(
                CreateEnrichment(
                    "enrichment-duplicate-property",
                    rank: 1,
                    RegulatoryEvidenceEnrichmentStatuses.Verified)),
            JsonOptions);
        var ambiguous = json.Replace(
            "\"enrichmentId\":\"enrichment-duplicate-property\"",
            "\"enrichmentId\":\"enrichment-duplicate-property\"," +
            "\"EnrichmentId\":\"tampered\"",
            StringComparison.Ordinal);

        var result = new ToolExecutionResultMapper().TryMapLegalResult(
            [CreateExecutionResult("legal.search_cnv_regulation", ambiguous)]);

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

    public static IEnumerable<object[]> InvalidEvidenceEnrichmentJsonCases()
    {
        yield return InvalidEnrichmentCase("null enrichment item", root =>
            root["evidenceEnrichments"] = JsonNode.Parse("[null]"));
        yield return InvalidEnrichmentCase("blank enrichment ID", root =>
            root["evidenceEnrichments"]![0]!["enrichmentId"] = " ");
        yield return InvalidEnrichmentCase("rank below contract", root =>
            root["evidenceEnrichments"]![0]!["rank"] = 0);
        yield return InvalidEnrichmentCase("rank above contract", root =>
            root["evidenceEnrichments"]![0]!["rank"] = 3);
        yield return InvalidEnrichmentCase("score below eligibility boundary", root =>
            root["evidenceEnrichments"]![0]!["score"] = 0.39);
        yield return InvalidEnrichmentCase("unknown enrichment status", root =>
            root["evidenceEnrichments"]![0]!["status"] = "Trusted");
        yield return InvalidEnrichmentCase("null original snapshot", root =>
            root["evidenceEnrichments"]![0]!["original"] = null);
        yield return InvalidEnrichmentCase("null original snippet", root =>
            root["evidenceEnrichments"]![0]!["original"]!["snippet"] = null);
        yield return InvalidEnrichmentCase("null original citation", root =>
            root["evidenceEnrichments"]![0]!["original"]!["citation"] = null);
        yield return InvalidEnrichmentCase("blank original citation source", root =>
            root["evidenceEnrichments"]![0]!["original"]!["citation"]!["source"] = " ");
        yield return InvalidEnrichmentCase("blank original citation title", root =>
            root["evidenceEnrichments"]![0]!["original"]!["citation"]!["title"] = " ");
        yield return InvalidEnrichmentCase("null limitations", root =>
            root["evidenceEnrichments"]![0]!["limitations"] = null);
        yield return InvalidEnrichmentCase("null limitation item", root =>
            root["evidenceEnrichments"]![0]!["limitations"] =
                JsonNode.Parse("[null]"));
        yield return InvalidEnrichmentCase("document text above contract maximum", root =>
        {
            root["evidenceEnrichments"]![0]!["document"]!["text"] =
                new string('d', 12_001);
            root["evidenceEnrichments"]![0]!["document"]!["originalTextLength"] =
                12_001;
        });
        yield return InvalidEnrichmentCase("negative document original length", root =>
            root["evidenceEnrichments"]![0]!["document"]!["originalTextLength"] = -1);
        yield return InvalidEnrichmentCase("document original shorter than stored", root =>
            root["evidenceEnrichments"]![0]!["document"]!["originalTextLength"] = 1);
        yield return InvalidEnrichmentCase("document truncation flag missing", root =>
        {
            var document = root["evidenceEnrichments"]![0]!["document"]!;
            document["originalTextLength"] =
                document["text"]!.GetValue<string>().Length + 1;
            document["isTruncated"] = false;
        });
        yield return InvalidEnrichmentCase("document truncation flag contradictory", root =>
            root["evidenceEnrichments"]![0]!["document"]!["isTruncated"] = true);
        yield return InvalidEnrichmentCase("null document metadata", root =>
            root["evidenceEnrichments"]![0]!["document"]!["metadata"] = null);
        yield return InvalidEnrichmentCase("null document citations", root =>
            root["evidenceEnrichments"]![0]!["document"]!["citations"] = null);
        yield return InvalidEnrichmentCase("null document citation", root =>
            root["evidenceEnrichments"]![0]!["document"]!["citations"] =
                JsonNode.Parse("[null]"));
        yield return InvalidEnrichmentCase("non-conflict document identity mismatch", root =>
            root["evidenceEnrichments"]![0]!["document"]!["id"] =
                "different-document");
        yield return InvalidEnrichmentCase("article text above contract maximum", root =>
        {
            root["evidenceEnrichments"]![0]!["article"]!["text"] =
                new string('a', 6_001);
            root["evidenceEnrichments"]![0]!["article"]!["originalTextLength"] =
                6_001;
        });
        yield return InvalidEnrichmentCase("negative article original length", root =>
            root["evidenceEnrichments"]![0]!["article"]!["originalTextLength"] = -1);
        yield return InvalidEnrichmentCase("article original shorter than stored", root =>
            root["evidenceEnrichments"]![0]!["article"]!["originalTextLength"] = 1);
        yield return InvalidEnrichmentCase("article truncation flag missing", root =>
        {
            var article = root["evidenceEnrichments"]![0]!["article"]!;
            article["originalTextLength"] =
                article["text"]!.GetValue<string>().Length + 1;
            article["isTruncated"] = false;
        });
        yield return InvalidEnrichmentCase("article truncation flag contradictory", root =>
            root["evidenceEnrichments"]![0]!["article"]!["isTruncated"] = true);
        yield return InvalidEnrichmentCase("null article citation", root =>
            root["evidenceEnrichments"]![0]!["article"]!["citation"] = null);
        yield return InvalidEnrichmentCase("blank canonical article locator", root =>
            root["evidenceEnrichments"]![0]!["article"]!["citation"]!["article"] =
                " ");
        yield return InvalidEnrichmentCase("null audit item", root =>
            root["queryStrategy"]!["enrichments"] = JsonNode.Parse("[null]"));
        yield return InvalidEnrichmentCase("blank audit ID", root =>
            root["queryStrategy"]!["enrichments"]![0]!["enrichmentId"] = " ");
        yield return InvalidEnrichmentCase("invalid audit rank", root =>
            root["queryStrategy"]!["enrichments"]![0]!["rank"] = 0);
        yield return InvalidEnrichmentCase("blank audit candidate key", root =>
            root["queryStrategy"]!["enrichments"]![0]!["candidateKey"] = " ");
        yield return InvalidEnrichmentCase("null audit contributing indices", root =>
            root["queryStrategy"]!["enrichments"]![0]!["contributingQueryIndices"] = null);
        yield return InvalidEnrichmentCase("invalid audit contributing index", root =>
            root["queryStrategy"]!["enrichments"]![0]!["contributingQueryIndices"] =
                JsonNode.Parse("[0]"));
        yield return InvalidEnrichmentCase("null document stage audit", root =>
            root["queryStrategy"]!["enrichments"]![0]!["document"] = null);
        yield return InvalidEnrichmentCase("invalid stage status", root =>
            root["queryStrategy"]!["enrichments"]![0]!["document"]!["status"] =
                "Retried");
        yield return InvalidEnrichmentCase("selected failed stage without retrieval", root =>
        {
            root["evidenceEnrichments"]![0]!["article"] = null;
            root["evidenceEnrichments"]![0]!["status"] =
                RegulatoryEvidenceEnrichmentStatuses.Partial;
            var audit = root["queryStrategy"]!["enrichments"]![0]!;
            audit["status"] = RegulatoryEvidenceEnrichmentStatuses.Partial;
            audit["article"]!["status"] =
                LegalCnvEnrichmentStageStatuses.Missing;
            audit["article"]!["attempted"] = false;
            audit["article"]!["originalTextLength"] = null;
        });
        yield return InvalidEnrichmentCase("negative stage original length", root =>
            root["queryStrategy"]!["enrichments"]![0]!["document"]![
                "originalTextLength"] = -1);
        yield return InvalidEnrichmentCase("stage length differs from snapshot", root =>
            root["queryStrategy"]!["enrichments"]![0]!["document"]![
                "originalTextLength"] = 999);
        yield return InvalidEnrichmentCase("null audit limitation codes", root =>
            root["queryStrategy"]!["enrichments"]![0]!["limitationCodes"] = null);
        yield return InvalidEnrichmentCase("null audit limitation code", root =>
            root["queryStrategy"]!["enrichments"]![0]!["limitationCodes"] =
                JsonNode.Parse("[null]"));
        yield return InvalidEnrichmentCase("audit status differs from enrichment", root =>
            root["queryStrategy"]!["enrichments"]![0]!["status"] =
                RegulatoryEvidenceEnrichmentStatuses.Conflict);
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

    private static object[] InvalidEnrichmentCase(
        string caseName,
        Action<JsonObject> mutate)
    {
        var root = JsonNode.Parse(
            JsonSerializer.Serialize(
                CreateEnrichedLegalResult(
                    CreateEnrichment(
                        "enrichment-valid",
                        rank: 1,
                        RegulatoryEvidenceEnrichmentStatuses.Verified)),
                JsonOptions))!
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
        string warning,
        string id)
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

        var reviewArea = new PossibleRegulatoryReviewArea(
            $"{id} review area",
            $"{id} review description",
            relevant ? "Warning" : "Info",
            [$"{id} signal"],
            [$"{id} citation"]);
        var evidenceReference = new LegalEvidenceReference(
            "CNV",
            $"{id} review citation",
            $"https://example.test/{id}",
            $"{id} citation",
            $"{id} snippet",
            "Emisoras",
            relevant ? 0.9 : 0.2);
        var reviewWarning = $"{id} review warning";
        var reviewLimitation = $"{id} review limitation";
        var review = new LegalAnalysisReviewResult(
            $"{id} review summary",
            [reviewArea, reviewArea],
            [evidenceReference, evidenceReference],
            [reviewWarning, reviewWarning],
            [reviewLimitation, reviewLimitation],
            UsedLlm: id == "strong",
            UsedFallback: id != "strong",
            Provider: $"{id}-provider",
            Model: $"{id}-model",
            FailureReason: null);

        var legalEvidence = new LegalEvidence(
            $"{id} regulation",
            $"{id} section",
            $"{id} finding",
            $"{id} source");

        return CreateLegalResult() with
        {
            HasComplianceRisk = false,
            RiskLevel = "NotEstablished",
            Summary = "Retrieval-only legal result.",
            Evidence = [legalEvidence, legalEvidence],
            Warnings = [warning],
            LegalReview = review,
            RequiresHumanReview = relevant,
            EvidenceAssessment = assessment
        };
    }

    private static LegalAgentResult CreateEnrichedLegalResult(
        RegulatoryEvidenceEnrichment enrichment,
        string id = "enriched")
    {
        var result = CreateAssessedLegalResult(
            relevance: "Strong",
            quality: "Strong",
            warning: $"{id} warning.",
            id: id);
        var strategy = (LegalQueryStrategyAudit)result.QueryStrategy!;
        return result with
        {
            EvidenceEnrichments = Array.AsReadOnly([enrichment]),
            QueryStrategy = strategy with
            {
                Enrichments = Array.AsReadOnly([CreateEnrichmentAudit(enrichment)])
            }
        };
    }

    private static RegulatoryEvidenceEnrichment CreateEnrichment(
        string enrichmentId,
        int rank,
        string status,
        bool includeDocument = true,
        bool includeArticle = true,
        IReadOnlyList<string>? limitations = null)
    {
        const string documentId = "document-stable";
        var citation = new RegulatoryEvidenceCitation(
            Source: "CNV",
            DocumentType: "Normas",
            ResolutionNumber: "622/2013",
            Title: "Normas CNV",
            Chapter: "I",
            Section: "1",
            Article: "Artículo 1",
            PublicationDate: "2013-09-05",
            Url: "https://example.test/cnv/1",
            QuotedText: "Texto citado");
        const string documentText = "Contexto canónico del documento.";
        const string articleText = "Contexto canónico del artículo.";
        return new RegulatoryEvidenceEnrichment(
            EnrichmentId: enrichmentId,
            DocumentId: documentId,
            ChunkId: "chunk-stable",
            Rank: rank,
            Score: 0.9,
            Original: new RegulatoryOriginalEvidence(
                "Fragmento original.",
                citation),
            Document: includeDocument
                ? new RegulatoryCanonicalDocument(
                    Id: documentId,
                    Source: "CNV",
                    DocumentType: "Normas",
                    ResolutionNumber: "622/2013",
                    Title: "Normas CNV",
                    PublicationDate: "2013-09-05",
                    EffectiveDate: "2013-09-05",
                    Url: "https://example.test/cnv",
                    Status: "current",
                    RequiresReview: false,
                    RetrievedAt: "2026-07-21T12:00:00Z",
                    Text: documentText,
                    OriginalTextLength: documentText.Length,
                    IsTruncated: false,
                    Metadata: new Dictionary<string, string>
                    {
                        ["jurisdiction"] = "AR"
                    },
                    Citations: Array.AsReadOnly([citation]))
                : null,
            Article: includeArticle
                ? new RegulatoryCanonicalArticle(
                    citation,
                    articleText,
                    Confidence: 0.95,
                    OriginalTextLength: articleText.Length,
                    IsTruncated: false)
                : null,
            Status: status,
            Limitations: Array.AsReadOnly(
                (limitations ?? ["limitación base"]).ToArray()));
    }

    private static RegulatoryEvidenceEnrichment CreateArticleOnlyEnrichment(
        string enrichmentId,
        string locator)
    {
        var enrichment = CreateEnrichment(
            enrichmentId,
            rank: 1,
            RegulatoryEvidenceEnrichmentStatuses.Partial,
            includeDocument: false,
            includeArticle: true);
        var originalCitation = enrichment.Original.Citation with
        {
            Article = locator
        };
        var articleCitation = enrichment.Article!.Citation with
        {
            Article = locator
        };
        return enrichment with
        {
            Original = enrichment.Original with
            {
                Citation = originalCitation
            },
            Article = enrichment.Article with
            {
                Citation = articleCitation
            }
        };
    }

    private static RegulatoryEvidenceEnrichment CreateUnavailableEnrichment(
        string enrichmentId,
        string documentId)
    {
        return CreateEnrichment(
            enrichmentId,
            rank: 1,
            RegulatoryEvidenceEnrichmentStatuses.Unavailable,
            includeDocument: false,
            includeArticle: false) with
        {
            DocumentId = documentId
        };
    }

    private static LegalAgentResult WithCandidateKey(
        LegalAgentResult result,
        string candidateKey)
    {
        var strategy = (LegalQueryStrategyAudit)result.QueryStrategy!;
        var audit = strategy.Enrichments!.Single() with
        {
            CandidateKey = candidateKey
        };
        return result with
        {
            QueryStrategy = strategy with
            {
                Enrichments = Array.AsReadOnly([audit])
            }
        };
    }

    private static void AssertConflictAggregateIsOrderIndependentAndRemappable(
        LegalAgentResult? forward,
        LegalAgentResult? reverse)
    {
        forward.Should().NotBeNull();
        reverse.Should().NotBeNull();
        forward!.EvidenceEnrichments.Should().BeEquivalentTo(
            reverse!.EvidenceEnrichments,
            options => options.WithStrictOrdering());
        var forwardAudit = (LegalQueryStrategyAudit)forward.QueryStrategy!;
        var reverseAudit = (LegalQueryStrategyAudit)reverse.QueryStrategy!;
        forwardAudit.Enrichments.Should().BeEquivalentTo(
            reverseAudit.Enrichments,
            options => options.WithStrictOrdering());
        forward.EvidenceEnrichments.Should().ContainSingle()
            .Which.Status.Should().Be(
                RegulatoryEvidenceEnrichmentStatuses.Conflict);
        forward.HasComplianceRisk.Should().BeFalse();
        forward.RiskLevel.Should().Be("NotEstablished");
        forward.RequiresHumanReview.Should().BeTrue();
        MapLegalResults(forward).Should().NotBeNull(
            "a merged conflict must remain valid mapper input");
    }

    private static LegalCnvEnrichmentAudit CreateEnrichmentAudit(
        RegulatoryEvidenceEnrichment enrichment)
    {
        var document = enrichment.Document;
        var article = enrichment.Article;
        return new LegalCnvEnrichmentAudit(
            EnrichmentId: enrichment.EnrichmentId,
            Rank: enrichment.Rank,
            CandidateKey: $"candidate-{enrichment.EnrichmentId}",
            Score: enrichment.Score,
            ContributingQueryIndices: Array.AsReadOnly([1]),
            Document: new LegalCnvEnrichmentStageAudit(
                Selected: true,
                Attempted: true,
                FromCache: false,
                Status: enrichment.Status ==
                        RegulatoryEvidenceEnrichmentStatuses.Conflict &&
                    document is not null
                        ? LegalCnvEnrichmentStageStatuses.Conflict
                        : document is null
                            ? LegalCnvEnrichmentStageStatuses.Missing
                            : LegalCnvEnrichmentStageStatuses.Succeeded,
                OriginalTextLength: document?.OriginalTextLength,
                IsTruncated: document?.IsTruncated ?? false),
            Article: new LegalCnvEnrichmentStageAudit(
                Selected: true,
                Attempted: true,
                FromCache: false,
                Status: article is null
                    ? LegalCnvEnrichmentStageStatuses.Missing
                    : LegalCnvEnrichmentStageStatuses.Succeeded,
                OriginalTextLength: article?.OriginalTextLength,
                IsTruncated: article?.IsTruncated ?? false),
            Status: enrichment.Status,
            LimitationCodes: Array.AsReadOnly(["test_limitation"]));
    }

    private static LegalAgentResult? MapLegalResults(
        params LegalAgentResult[] results)
    {
        return new ToolExecutionResultMapper().TryMapLegalResult(results
            .Select(result => CreateExecutionResult(
                "legal.search_cnv_regulation",
                JsonSerializer.Serialize(result, JsonOptions)))
            .ToArray());
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
