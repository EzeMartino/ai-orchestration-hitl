using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Legal.Cnv;
using Xunit;

namespace Orchestration.Tests.Agents.Legal;

public class FinancialAnalysisLegalCnvQueryStrategyTests
{
    private readonly FinancialAnalysisLegalCnvQueryStrategy _strategy = new();

    private static FinancialAnalysisContext CreateContext(
        IReadOnlyList<FinancialRiskSignal> signals,
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<string>? limitations = null)
    {
        return new FinancialAnalysisContext(
            Engine: "TestDataEngine",
            DocumentId: "doc-123",
            Company: "TestCorp",
            Ratios: Array.Empty<FinancialRatio>(),
            Comparisons: Array.Empty<FinancialPeriodComparison>(),
            RiskSignals: signals,
            RiskEvidence: Array.Empty<RiskEvidenceItem>(),
            Warnings: warnings ?? Array.Empty<string>(),
            Limitations: limitations ?? Array.Empty<string>()
        )
        {
            Execution = FinancialAnalysisExecution.FromStages(
                FinancialAnalysisOperations.All.Select(operation =>
                    new FinancialAnalysisStageExecution(
                        operation,
                        FinancialAnalysisExecutionStatus.Succeeded,
                        DurationMilliseconds: 1)))
        };
    }

    [Fact]
    public void BuildPlan_ContextualSignals_ReturnsAuditableOrderedDeduplicatedPlanCappedAtFour()
    {
        var context = CreateContext(
        [
            new FinancialRiskSignal(
                "liquidity_one",
                "Medium",
                "Q1",
                "Low working_capital.",
                Array.Empty<RiskEvidenceItem>()),
            new FinancialRiskSignal(
                "critical_leverage",
                "High",
                "Q1",
                "Extremely high debt.",
                Array.Empty<RiskEvidenceItem>()),
            new FinancialRiskSignal(
                "liquidity_two",
                "Medium",
                "Q1",
                "Low current_ratio.",
                Array.Empty<RiskEvidenceItem>())
        ]);
        var dataEvidence = Classify(context);

        var result = _strategy.BuildPlan(context, dataEvidence);

        result.StrategyVersion.Should().Be("financial_analysis_v2");
        result.Source.Should().Be(LegalCnvQuerySources.Contextual);
        result.FallbackReason.Should().BeNull();
        result.DataEvidence.Should().BeEquivalentTo(dataEvidence);
        result.DataEvidence.Should().NotBeSameAs(dataEvidence);
        result.Queries.Select(query => query.Query).Should().Equal(
            "hecho relevante información al mercado emisoras",
            "endeudamiento información al mercado estados financieros",
            "obligaciones negociables endeudamiento régimen informativo",
            "régimen informativo estados financieros liquidez"
        );
        result.Queries.Should().HaveCount(4);
        result.Queries.Select(query => query.Query).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void BuildPlan_CanonicalUnusableEvidence_PreservesCanonicalReasons()
    {
        var context = CreateContext(
        [
            new FinancialRiskSignal(
                "liquidity_risk",
                "Medium",
                "Q1",
                "Low current ratio.",
                Array.Empty<RiskEvidenceItem>())
        ]);
        var failedSignalsContext = context with
        {
            Execution = FinancialAnalysisExecution.FromStages(
            [
                new FinancialAnalysisStageExecution(
                    FinancialAnalysisOperations.Signals,
                    FinancialAnalysisExecutionStatus.Failed,
                    DurationMilliseconds: 1,
                    FinancialAnalysisFailureCodes.PythonInvocationFailed)
            ])
        };
        var noSignalsContext = CreateContext([]);
        var legacyContext = context with
        {
            Execution = FinancialAnalysisExecution.LegacyUnknown
        };
        (FinancialAnalysisContext? Context, LegalDataEvidenceContext Evidence)[] cases =
        [
            (context, Classify(LegalDataToolStatuses.Failed, context)),
            (context, Classify(LegalDataToolStatuses.Absent, context)),
            (null, Classify(LegalDataToolStatuses.Executed, null)),
            (failedSignalsContext, Classify(
                LegalDataToolStatuses.Executed,
                failedSignalsContext)),
            (noSignalsContext, Classify(
                LegalDataToolStatuses.Executed,
                noSignalsContext)),
            (legacyContext, Classify(
                LegalDataToolStatuses.Executed,
                legacyContext))
        ];

        foreach (var (financialAnalysis, dataEvidence) in cases)
        {
            var result = _strategy.BuildPlan(financialAnalysis, dataEvidence);

            AssertGenericFallback(result);
            result.FallbackReason.Should().Be(dataEvidence.FallbackReason);
            result.DataEvidence.Should().BeEquivalentTo(dataEvidence);
            result.DataEvidence.Should().NotBeSameAs(dataEvidence);
        }
    }

    [Fact]
    public void BuildPlan_UnsupportedSignal_ReturnsSignalsUnmappedFallback()
    {
        var context = CreateContext(
        [
            new FinancialRiskSignal(
                "unsupported_signal",
                "Medium",
                "Q1",
                "No mapped category.",
                Array.Empty<RiskEvidenceItem>())
        ]);

        var dataEvidence = Classify(context);

        var result = _strategy.BuildPlan(context, dataEvidence);

        AssertGenericFallback(result);
        result.FallbackReason.Should().Be(LegalCnvFallbackReasons.SignalsUnmapped);
        result.DataEvidence.Should().BeEquivalentTo(dataEvidence);
        result.DataEvidence.Should().NotBeSameAs(dataEvidence);
        result.DataEvidence.CanUseSignals.Should().BeTrue();
        result.DataEvidence.FallbackReason.Should().BeNull();
    }

    [Fact]
    public void BuildPlan_WarningsAndLimitations_DoNotChangeSignalQueries()
    {
        FinancialRiskSignal[] signals =
        [
            new FinancialRiskSignal(
                "liquidity_risk",
                "Medium",
                "Q1",
                "Low current ratio.",
                Array.Empty<RiskEvidenceItem>())
        ];
        var cleanContext = CreateContext(signals);
        var noisyContext = CreateContext(
            signals,
            warnings: ["Sensitive warning should not drive legal queries."],
            limitations: ["Sensitive limitation should not drive legal queries."]
        );

        var clean = _strategy.BuildPlan(cleanContext, Classify(cleanContext));
        var noisy = _strategy.BuildPlan(noisyContext, Classify(noisyContext));

        noisy.Source.Should().Be(LegalCnvQuerySources.Contextual);
        noisy.Queries.Should().BeEquivalentTo(
            clean.Queries,
            options => options.WithStrictOrdering()
        );
    }

    [Fact]
    public void BuildPlan_WarningsAndLimitationsWithoutSignals_NeverReturnsContextualQueries()
    {
        var context = CreateContext(
            signals: [],
            warnings: ["Data quality warning."],
            limitations: ["Partial ratios only."]
        );

        var result = _strategy.BuildPlan(context, Classify(context));

        AssertGenericFallback(result);
        result.FallbackReason.Should().Be(LegalCnvFallbackReasons.NoSpecificSignals);
        result.Queries.SelectMany(query => query.RelatedFinancialSignals)
            .Should().NotContain("warning");
    }

    [Fact]
    public void BuildPlan_UsableEvidenceWithoutFinancialAnalysis_FailsClosed()
    {
        var contradictoryEvidence = new LegalDataEvidenceContext(
            CanUseSignals: true,
            FallbackReason: null,
            DataToolStatus: LegalDataToolStatuses.Executed,
            FinancialAnalysisStatus: FinancialAnalysisExecutionStatus.Succeeded,
            FailedStages: []
        );

        var result = _strategy.BuildPlan(
            financialAnalysis: null,
            contradictoryEvidence
        );

        AssertGenericFallback(result);
        result.FallbackReason.Should().Be(
            LegalCnvFallbackReasons.LegacyOrAmbiguousExecution);
        AssertSanitizedAmbiguousEvidence(
            result,
            expectedFinancialAnalysisStatus: null
        );
    }

    [Fact]
    public void BuildPlan_UsableEvidenceWithInconsistentExecution_FailsClosed()
    {
        var context = CreateContext(
        [
            new FinancialRiskSignal(
                "liquidity_risk",
                "Medium",
                "Q1",
                "Low current ratio.",
                Array.Empty<RiskEvidenceItem>())
        ]) with
        {
            Execution = FinancialAnalysisExecution.LegacyUnknown
        };
        var contradictoryEvidence = new LegalDataEvidenceContext(
            CanUseSignals: true,
            FallbackReason: null,
            DataToolStatus: LegalDataToolStatuses.Executed,
            FinancialAnalysisStatus: FinancialAnalysisExecutionStatus.Succeeded,
            FailedStages: []
        );

        var result = _strategy.BuildPlan(context, contradictoryEvidence);

        AssertGenericFallback(result);
        result.FallbackReason.Should().Be(
            LegalCnvFallbackReasons.LegacyOrAmbiguousExecution);
        AssertSanitizedAmbiguousEvidence(
            result,
            FinancialAnalysisExecutionStatus.LegacyUnknown
        );
    }

    [Fact]
    public void BuildPlan_ArbitraryAuditInput_IsSanitizedWithoutSensitiveLeakage()
    {
        var context = CreateContext(
        [
            new FinancialRiskSignal(
                "liquidity_risk",
                "Medium",
                "Q1",
                "Low current ratio.",
                Array.Empty<RiskEvidenceItem>())
        ]);
        var untrustedEvidence = new LegalDataEvidenceContext(
            CanUseSignals: false,
            FallbackReason: "sensitive arbitrary fallback reason",
            DataToolStatus: "sensitive arbitrary data status",
            FinancialAnalysisStatus: FinancialAnalysisExecutionStatus.Succeeded,
            FailedStages:
            [
                new LegalDataStageFailureAudit(
                    "sensitive arbitrary operation",
                    "sensitive arbitrary failure code")
            ]
        );

        var result = _strategy.BuildPlan(context, untrustedEvidence);

        AssertGenericFallback(result);
        AssertSanitizedAmbiguousEvidence(
            result,
            FinancialAnalysisExecutionStatus.Succeeded
        );
        result.ToString().Should().NotContain("sensitive arbitrary");
    }

    [Theory]
    [InlineData(LegalCnvFallbackReasons.DataToolFailed)]
    [InlineData(LegalCnvFallbackReasons.SignalsUnmapped)]
    public void BuildPlan_InconsistentFallbackReason_IsSanitized(
        string inconsistentReason)
    {
        var context = CreateContext(
        [
            new FinancialRiskSignal(
                "liquidity_risk",
                "Medium",
                "Q1",
                "Low current ratio.",
                Array.Empty<RiskEvidenceItem>())
        ]);
        var inconsistentEvidence = new LegalDataEvidenceContext(
            CanUseSignals: false,
            FallbackReason: inconsistentReason,
            DataToolStatus: LegalDataToolStatuses.Executed,
            FinancialAnalysisStatus: FinancialAnalysisExecutionStatus.Succeeded,
            FailedStages: []
        );

        var result = _strategy.BuildPlan(context, inconsistentEvidence);

        AssertGenericFallback(result);
        AssertSanitizedAmbiguousEvidence(
            result,
            FinancialAnalysisExecutionStatus.Succeeded
        );
    }

    [Fact]
    public void BuildPlan_DataToolFailureMarkedUsable_IsSanitized()
    {
        var context = CreateContext(
        [
            new FinancialRiskSignal(
                "liquidity_risk",
                "Medium",
                "Q1",
                "Low current ratio.",
                Array.Empty<RiskEvidenceItem>())
        ]);
        var inconsistentEvidence = new LegalDataEvidenceContext(
            CanUseSignals: true,
            FallbackReason: null,
            DataToolStatus: LegalDataToolStatuses.Failed,
            FinancialAnalysisStatus: FinancialAnalysisExecutionStatus.Succeeded,
            FailedStages: []
        );

        var result = _strategy.BuildPlan(context, inconsistentEvidence);

        AssertGenericFallback(result);
        AssertSanitizedAmbiguousEvidence(
            result,
            FinancialAnalysisExecutionStatus.Succeeded
        );
    }

    [Fact]
    public void BuildPlan_NullRuntimeEvidence_FailsClosedWithCanonicalContextMetadata()
    {
        var context = CreateContext(
        [
            new FinancialRiskSignal(
                "liquidity_risk",
                "Medium",
                "Q1",
                "Low current ratio.",
                Array.Empty<RiskEvidenceItem>())
        ]);

        var result = _strategy.BuildPlan(context, dataEvidence: null!);

        AssertGenericFallback(result);
        AssertSanitizedAmbiguousEvidence(
            result,
            FinancialAnalysisExecutionStatus.Succeeded
        );
    }

    [Fact]
    public void LegalCnvQueryPlan_ConstructorSnapshotsMutableAuditCollectionsAsReadOnly()
    {
        var mutableFailures = new List<LegalDataStageFailureAudit>
        {
            new(
                FinancialAnalysisOperations.Ratios,
                FinancialAnalysisFailureCodes.PythonInvocationFailed)
        };
        var mutableQueries = new List<LegalCnvQuery>
        {
            new(
                "test query",
                "test_area",
                "Test reason.",
                Array.Empty<string>())
        };
        var sourceEvidence = new LegalDataEvidenceContext(
            CanUseSignals: false,
            FallbackReason: LegalCnvFallbackReasons.DataToolFailed,
            DataToolStatus: LegalDataToolStatuses.Failed,
            FinancialAnalysisStatus: FinancialAnalysisExecutionStatus.Failed,
            FailedStages: mutableFailures
        );

        var result = new LegalCnvQueryPlan(
            "financial_analysis_v2",
            LegalCnvQuerySources.Fallback,
            LegalCnvFallbackReasons.DataToolFailed,
            sourceEvidence,
            mutableQueries
        );

        mutableFailures.Add(new LegalDataStageFailureAudit(
            FinancialAnalysisOperations.Comparisons,
            FinancialAnalysisFailureCodes.PythonResponseInvalid));
        mutableQueries.Clear();

        result.DataEvidence.Should().NotBeSameAs(sourceEvidence);
        result.DataEvidence.FailedStages.Should().ContainSingle();
        result.Queries.Should().ContainSingle();

        var failedStageSnapshot = result.DataEvidence.FailedStages
            .Should().BeAssignableTo<System.Collections.IList>().Subject;
        var querySnapshot = result.Queries
            .Should().BeAssignableTo<System.Collections.IList>().Subject;
        failedStageSnapshot.IsReadOnly.Should().BeTrue();
        querySnapshot.IsReadOnly.Should().BeTrue();

        Action addFailure = () =>
        {
            failedStageSnapshot.Add(new LegalDataStageFailureAudit(
                FinancialAnalysisOperations.Summary,
                FinancialAnalysisFailureCodes.UnexpectedFailure));
        };
        Action addQuery = () =>
        {
            querySnapshot.Add(mutableQueries.FirstOrDefault());
        };
        addFailure.Should().Throw<NotSupportedException>();
        addQuery.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void BuildQueries_LegacyAdapter_MatchesBuildPlanQueries()
    {
        var context = CreateContext(
        [
            new FinancialRiskSignal(
                "liquidity_risk",
                "Medium",
                "Q1",
                "Low current ratio.",
                Array.Empty<RiskEvidenceItem>())
        ]);
        var plan = _strategy.BuildPlan(context, Classify(context));

        var legacyQueries = _strategy.BuildQueries(context);

        legacyQueries.Should().BeEquivalentTo(
            plan.Queries,
            options => options.WithStrictOrdering()
        );
    }

    [Fact]
    public void BuildQueries_Should_return_fallback_query_when_context_is_null()
    {
        var result = _strategy.BuildQueries(null);

        result.Should().HaveCount(1);
        result[0].Query.Should().Be("régimen informativo estados financieros emisoras");
        result[0].RegulationArea.Should().Be("general_reporting");
        result[0].Reason.Should().Contain("No había señales específicas de riesgo financiero");
        result[0].RelatedFinancialSignals.Should().BeEmpty();
    }

    [Fact]
    public void BuildQueries_Should_return_fallback_query_when_no_risk_signals()
    {
        var context = CreateContext(Array.Empty<FinancialRiskSignal>());
        var result = _strategy.BuildQueries(context);

        result.Should().HaveCount(1);
        result[0].Query.Should().Be("régimen informativo estados financieros emisoras");
        result[0].RegulationArea.Should().Be("general_reporting");
        result[0].RelatedFinancialSignals.Should().BeEmpty();
    }

    [Fact]
    public void BuildQueries_LiquidityRisk_Should_create_liquidity_queries()
    {
        var signals = new[]
        {
            new FinancialRiskSignal("liquidity_risk", "Medium", "Q1", "The company has low liquidity.", Array.Empty<RiskEvidenceItem>())
        };
        var context = CreateContext(signals);
        var result = _strategy.BuildQueries(context);

        result.Should().Contain(q => q.Query == "régimen informativo estados financieros liquidez");
        result.Should().Contain(q => q.Query == "información financiera periódica estados contables");
        result.Any(q => q.RelatedFinancialSignals.Contains("liquidity_risk")).Should().BeTrue();
    }

    [Fact]
    public void BuildQueries_LeverageRisk_Should_create_indebtedness_queries()
    {
        var signals = new[]
        {
            new FinancialRiskSignal("leverage_warning", "Medium", "Q1", "High debt levels found.", Array.Empty<RiskEvidenceItem>())
        };
        var context = CreateContext(signals);
        var result = _strategy.BuildQueries(context);

        result.Should().Contain(q => q.Query == "endeudamiento información al mercado estados financieros");
        result.Should().Contain(q => q.Query == "obligaciones negociables endeudamiento régimen informativo");
        result.Any(q => q.RelatedFinancialSignals.Contains("leverage_warning")).Should().BeTrue();
    }

    [Fact]
    public void BuildQueries_MarginDeterioration_Should_create_profitability_queries()
    {
        var signals = new[]
        {
            new FinancialRiskSignal("margin_deterioration", "Medium", "Q1", "Gross margins decreased significantly.", Array.Empty<RiskEvidenceItem>())
        };
        var context = CreateContext(signals);
        var result = _strategy.BuildQueries(context);

        result.Should().Contain(q => q.Query == "resultados estados financieros información periódica emisoras");
        result.Should().Contain(q => q.Query == "hecho relevante deterioro resultados información al mercado");
    }

    [Fact]
    public void BuildQueries_DataQualityWarning_Should_create_reporting_duties_queries()
    {
        var signals = new[]
        {
            new FinancialRiskSignal("missing metrics", "Medium", "Q1", "Some parameters are missing.", Array.Empty<RiskEvidenceItem>())
        };
        var context = CreateContext(signals);
        var result = _strategy.BuildQueries(context);

        result.Should().Contain(q => q.Query == "deberes informativos emisoras información periódica");
        result.Should().Contain(q => q.Query == "régimen informativo estados financieros emisoras");
    }

    [Fact]
    public void BuildQueries_HighSeverityMaterialDeterioration_Should_create_hecho_relevante_query()
    {
        var signals = new[]
        {
            new FinancialRiskSignal("material deterioration", "High", "Q1", "Critical deterioration.", Array.Empty<RiskEvidenceItem>())
        };
        var context = CreateContext(signals);
        var result = _strategy.BuildQueries(context);

        result.Should().Contain(q => q.Query == "hecho relevante información al mercado emisoras");
    }

    [Fact]
    public void BuildQueries_Should_deduplicate_repeated_queries()
    {
        var signals = new[]
        {
            new FinancialRiskSignal("liquidity_one", "Medium", "Q1", "Low working_capital.", Array.Empty<RiskEvidenceItem>()),
            new FinancialRiskSignal("liquidity_two", "Medium", "Q1", "Low current_ratio.", Array.Empty<RiskEvidenceItem>())
        };
        var context = CreateContext(signals);
        var result = _strategy.BuildQueries(context);

        result.Count(q => q.Query == "régimen informativo estados financieros liquidez").Should().Be(1);
        result.Count(q => q.Query == "información financiera periódica estados contables").Should().Be(1);
    }

    [Fact]
    public void BuildQueries_Should_limit_to_max_four_queries()
    {
        var signals = new[]
        {
            new FinancialRiskSignal("liquidity", "Medium", "Q1", "Low working_capital.", Array.Empty<RiskEvidenceItem>()),
            new FinancialRiskSignal("leverage", "Medium", "Q1", "High debt.", Array.Empty<RiskEvidenceItem>()),
            new FinancialRiskSignal("profitability", "Medium", "Q1", "Ebitda warning.", Array.Empty<RiskEvidenceItem>())
        };
        var context = CreateContext(signals);
        var result = _strategy.BuildQueries(context);

        result.Count.Should().BeLessThanOrEqualTo(4);
    }

    [Fact]
    public void BuildQueries_Should_prioritize_High_severity_signals()
    {
        var signals = new[]
        {
            new FinancialRiskSignal("liquidity", "Medium", "Q1", "Low working_capital.", Array.Empty<RiskEvidenceItem>()),
            new FinancialRiskSignal("critical_leverage", "High", "Q1", "Extremely high debt.", Array.Empty<RiskEvidenceItem>())
        };
        var context = CreateContext(signals);
        var result = _strategy.BuildQueries(context);

        // Since critical_leverage is High severity, leverage-related queries should come before liquidity-related ones!
        result.Count.Should().BeLessThanOrEqualTo(4);
        var list = result.ToList();
        var indexLeverage = list.FindIndex(q => q.Query.Contains("endeudamiento"));
        var indexLiquidity = list.FindIndex(q => q.Query.Contains("liquidez"));

        indexLeverage.Should().BeLessThan(indexLiquidity);
    }

    [Fact]
    public void BuildQueries_Should_include_related_financial_signals_in_reasons_and_lists()
    {
        var signals = new[]
        {
            new FinancialRiskSignal("liquidity_signal", "Medium", "Q1", "Low working_capital.", Array.Empty<RiskEvidenceItem>())
        };
        var context = CreateContext(signals);
        var result = _strategy.BuildQueries(context);

        var query = result.First(q => q.Query.Contains("liquidez"));
        query.RelatedFinancialSignals.Should().Contain("liquidity_signal");
    }

    [Fact]
    public void BuildQueries_Should_use_enriched_metric_fields_before_text_fallback()
    {
        var signals = new[]
        {
            new FinancialRiskSignal(
                Name: "threshold_crossed",
                Severity: "Medium",
                Period: "Q1",
                Summary: "Configured threshold crossed.",
                Evidence: Array.Empty<RiskEvidenceItem>(),
                Metric: "current_ratio",
                ThresholdCode: "LOW_CURRENT_RATIO"
            )
        };
        var context = CreateContext(signals);

        var result = _strategy.BuildQueries(context);

        result.Should().Contain(q => q.Query.Contains("liquidez", StringComparison.OrdinalIgnoreCase));
    }

    private static LegalDataEvidenceContext Classify(
        FinancialAnalysisContext context)
    {
        return Classify(LegalDataToolStatuses.Executed, context);
    }

    private static LegalDataEvidenceContext Classify(
        string dataToolStatus,
        FinancialAnalysisContext? context)
    {
        return LegalDataEvidenceClassifier.Classify(dataToolStatus, context);
    }

    private static void AssertSanitizedAmbiguousEvidence(
        LegalCnvQueryPlan result,
        FinancialAnalysisExecutionStatus? expectedFinancialAnalysisStatus,
        params LegalDataStageFailureAudit[] expectedFailedStages)
    {
        result.FallbackReason.Should().Be(LegalCnvFallbackReasons.LegacyOrAmbiguousExecution);
        result.DataEvidence.CanUseSignals.Should().BeFalse();
        result.DataEvidence.FallbackReason.Should().Be(LegalCnvFallbackReasons.LegacyOrAmbiguousExecution);
        result.DataEvidence.DataToolStatus.Should().Be(LegalDataToolStatuses.Unknown);
        result.DataEvidence.FinancialAnalysisStatus.Should().Be(expectedFinancialAnalysisStatus);
        result.DataEvidence.FailedStages.Should().Equal(expectedFailedStages);
    }

    private static void AssertGenericFallback(LegalCnvQueryPlan result)
    {
        result.StrategyVersion.Should().Be("financial_analysis_v2");
        result.Source.Should().Be(LegalCnvQuerySources.Fallback);
        var query = result.Queries.Should().ContainSingle().Subject;
        query.Query.Should().Be("régimen informativo estados financieros emisoras");
        query.RegulationArea.Should().Be("general_reporting");
        query.RelatedFinancialSignals.Should().BeEmpty();
    }
}
