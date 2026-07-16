using FluentAssertions;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Legal.Cnv;

namespace Orchestration.Tests.Agents.Legal;

public sealed class LegalDataEvidenceClassifierTests
{
    [Fact]
    public void Classify_DataToolFailed_TakesPrecedenceOverSucceededAnalysis()
    {
        var financialAnalysis = CreateFinancialAnalysis(
            FinancialAnalysisExecutionStatus.Succeeded,
            [Stage(FinancialAnalysisOperations.Signals, FinancialAnalysisExecutionStatus.Succeeded)]
        );

        var result = LegalDataEvidenceClassifier.Classify(
            LegalDataToolStatuses.Failed,
            financialAnalysis
        );

        result.CanUseSignals.Should().BeFalse();
        result.FallbackReason.Should().Be(LegalCnvFallbackReasons.DataToolFailed);
        result.DataToolStatus.Should().Be(LegalDataToolStatuses.Failed);
        result.FinancialAnalysisStatus.Should().Be(FinancialAnalysisExecutionStatus.Succeeded);
        result.FailedStages.Should().BeEmpty();
    }

    [Fact]
    public void Classify_DataStageAbsent_ReturnsAbsentFallback()
    {
        var result = LegalDataEvidenceClassifier.Classify(
            LegalDataToolStatuses.Absent,
            CreateFinancialAnalysis()
        );

        result.CanUseSignals.Should().BeFalse();
        result.FallbackReason.Should().Be(LegalCnvFallbackReasons.DataStageAbsent);
        result.DataToolStatus.Should().Be(LegalDataToolStatuses.Absent);
    }

    [Fact]
    public void Classify_FinancialAnalysisMissing_ReturnsMissingFallback()
    {
        var result = LegalDataEvidenceClassifier.Classify(
            LegalDataToolStatuses.Executed,
            financialAnalysis: null
        );

        result.CanUseSignals.Should().BeFalse();
        result.FallbackReason.Should().Be(LegalCnvFallbackReasons.FinancialAnalysisMissing);
        result.FinancialAnalysisStatus.Should().BeNull();
        result.FailedStages.Should().BeEmpty();
    }

    [Fact]
    public void Classify_LegacyOverallStatus_ReturnsAmbiguousFallback()
    {
        var result = LegalDataEvidenceClassifier.Classify(
            LegalDataToolStatuses.Executed,
            CreateFinancialAnalysis(FinancialAnalysisExecutionStatus.LegacyUnknown)
        );

        result.CanUseSignals.Should().BeFalse();
        result.FallbackReason.Should().Be(LegalCnvFallbackReasons.LegacyOrAmbiguousExecution);
        result.FinancialAnalysisStatus.Should().Be(FinancialAnalysisExecutionStatus.LegacyUnknown);
    }

    public static TheoryData<IReadOnlyList<FinancialAnalysisStageExecution>> AmbiguousSignalStages =>
        new()
        {
            { [] },
            {
                [
                Stage(FinancialAnalysisOperations.Signals, FinancialAnalysisExecutionStatus.Succeeded),
                Stage(FinancialAnalysisOperations.Signals, FinancialAnalysisExecutionStatus.Succeeded)
                ]
            },
            { [Stage(FinancialAnalysisOperations.Signals, FinancialAnalysisExecutionStatus.LegacyUnknown)] }
        };

    [Theory]
    [MemberData(nameof(AmbiguousSignalStages))]
    public void Classify_AmbiguousSignalStageMetadata_ReturnsAmbiguousFallback(
        IReadOnlyList<FinancialAnalysisStageExecution> stages)
    {
        var result = LegalDataEvidenceClassifier.Classify(
            LegalDataToolStatuses.Executed,
            CreateFinancialAnalysis(FinancialAnalysisExecutionStatus.Degraded, stages)
        );

        result.CanUseSignals.Should().BeFalse();
        result.FallbackReason.Should().Be(LegalCnvFallbackReasons.LegacyOrAmbiguousExecution);
    }

    [Fact]
    public void Classify_FailedSignalsStage_ReturnsSignalsFailureFallback()
    {
        var result = LegalDataEvidenceClassifier.Classify(
            LegalDataToolStatuses.Executed,
            CreateFinancialAnalysis(
                FinancialAnalysisExecutionStatus.Failed,
                [Stage(
                    FinancialAnalysisOperations.Signals,
                    FinancialAnalysisExecutionStatus.Failed,
                    FinancialAnalysisFailureCodes.PythonInvocationFailed)]
            )
        );

        result.CanUseSignals.Should().BeFalse();
        result.FallbackReason.Should().Be(LegalCnvFallbackReasons.SignalsStageFailed);
    }

    [Fact]
    public void Classify_NoRiskSignals_ReturnsNoSpecificSignalsFallback()
    {
        var result = LegalDataEvidenceClassifier.Classify(
            LegalDataToolStatuses.Executed,
            CreateFinancialAnalysis(riskSignals: [])
        );

        result.CanUseSignals.Should().BeFalse();
        result.FallbackReason.Should().Be(LegalCnvFallbackReasons.NoSpecificSignals);
    }

    [Fact]
    public void Classify_DegradedAnalysisWithSuccessfulSignals_ReturnsUsableContext()
    {
        var result = LegalDataEvidenceClassifier.Classify(
            LegalDataToolStatuses.Executed,
            CreateFinancialAnalysis(
                FinancialAnalysisExecutionStatus.Degraded,
                [
                    Stage(FinancialAnalysisOperations.Ratios, FinancialAnalysisExecutionStatus.Failed),
                    Stage(FinancialAnalysisOperations.Signals, FinancialAnalysisExecutionStatus.Succeeded)
                ]
            )
        );

        result.CanUseSignals.Should().BeTrue();
        result.FallbackReason.Should().BeNull();
        result.DataToolStatus.Should().Be(LegalDataToolStatuses.Executed);
        result.FinancialAnalysisStatus.Should().Be(FinancialAnalysisExecutionStatus.Degraded);
    }

    [Fact]
    public void Classify_FailedStageAudit_AllowListsOperationsAndSanitizesFailureCodes()
    {
        var result = LegalDataEvidenceClassifier.Classify(
            LegalDataToolStatuses.Executed,
            CreateFinancialAnalysis(
                FinancialAnalysisExecutionStatus.Degraded,
                [
                    Stage(
                        FinancialAnalysisOperations.Ratios,
                        FinancialAnalysisExecutionStatus.Failed,
                        FinancialAnalysisFailureCodes.PythonResponseInvalid),
                    Stage(
                        FinancialAnalysisOperations.Comparisons,
                        FinancialAnalysisExecutionStatus.Failed,
                        "sensitive arbitrary failure text"),
                    Stage(
                        "sensitive arbitrary operation",
                        FinancialAnalysisExecutionStatus.Failed,
                        "sensitive arbitrary failure text"),
                    Stage(FinancialAnalysisOperations.Signals, FinancialAnalysisExecutionStatus.Succeeded)
                ]
            )
        );

        result.CanUseSignals.Should().BeTrue();
        result.FailedStages.Should().BeEquivalentTo(
            [
                new LegalDataStageFailureAudit(
                    FinancialAnalysisOperations.Ratios,
                    FinancialAnalysisFailureCodes.PythonResponseInvalid),
                new LegalDataStageFailureAudit(
                    FinancialAnalysisOperations.Comparisons,
                    FinancialAnalysisFailureCodes.UnexpectedFailure)
            ],
            options => options.WithStrictOrdering()
        );
        result.FailedStages.Should().NotContain(stage =>
            stage.Operation.Contains("sensitive", StringComparison.Ordinal) ||
            stage.FailureCode.Contains("sensitive", StringComparison.Ordinal));
    }

    private static FinancialAnalysisContext CreateFinancialAnalysis(
        FinancialAnalysisExecutionStatus overallStatus = FinancialAnalysisExecutionStatus.Succeeded,
        IReadOnlyList<FinancialAnalysisStageExecution>? stages = null,
        IReadOnlyList<FinancialRiskSignal>? riskSignals = null)
    {
        return new FinancialAnalysisContext(
            Engine: "Test engine",
            DocumentId: "test-document",
            Company: "Test company",
            Ratios: [],
            Comparisons: [],
            RiskSignals: riskSignals ?? [CreateRiskSignal()],
            RiskEvidence: [],
            Warnings: [],
            Limitations: []
        )
        {
            Execution = new FinancialAnalysisExecution(
                overallStatus,
                stages ??
                [
                    Stage(
                        FinancialAnalysisOperations.Signals,
                        FinancialAnalysisExecutionStatus.Succeeded)
                ]
            )
        };
    }

    private static FinancialRiskSignal CreateRiskSignal()
    {
        return new FinancialRiskSignal(
            Name: "liquidity_risk",
            Severity: "High",
            Period: "2025",
            Summary: "Liquidity risk signal.",
            Evidence: []
        );
    }

    private static FinancialAnalysisStageExecution Stage(
        string operation,
        FinancialAnalysisExecutionStatus status,
        string? failureCode = null)
    {
        return new FinancialAnalysisStageExecution(
            operation,
            status,
            DurationMilliseconds: 1,
            failureCode
        );
    }
}
