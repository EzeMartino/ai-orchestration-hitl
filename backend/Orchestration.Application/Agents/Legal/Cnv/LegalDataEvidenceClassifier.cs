using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Application.Agents.Legal.Cnv;

public static class LegalDataEvidenceClassifier
{
    public static LegalDataEvidenceContext Classify(
        string dataToolStatus,
        FinancialAnalysisContext? financialAnalysis)
    {
        var financialAnalysisStatus = financialAnalysis?.Execution.OverallStatus;
        var failedStages = MapAllowlistedFailures(financialAnalysis);

        LegalDataEvidenceContext Fallback(string reason)
        {
            return new LegalDataEvidenceContext(
                CanUseSignals: false,
                FallbackReason: reason,
                DataToolStatus: dataToolStatus,
                FinancialAnalysisStatus: financialAnalysisStatus,
                FailedStages: failedStages
            );
        }

        if (string.Equals(
                dataToolStatus,
                LegalDataToolStatuses.Failed,
                StringComparison.Ordinal))
        {
            return Fallback(LegalCnvFallbackReasons.DataToolFailed);
        }

        if (string.Equals(
                dataToolStatus,
                LegalDataToolStatuses.Absent,
                StringComparison.Ordinal))
        {
            return Fallback(LegalCnvFallbackReasons.DataStageAbsent);
        }

        if (financialAnalysis is null)
        {
            return Fallback(LegalCnvFallbackReasons.FinancialAnalysisMissing);
        }

        if (financialAnalysis.Execution.OverallStatus ==
            FinancialAnalysisExecutionStatus.LegacyUnknown)
        {
            return Fallback(LegalCnvFallbackReasons.LegacyOrAmbiguousExecution);
        }

        var signalStages = financialAnalysis.Execution.Stages
            .Where(stage => string.Equals(
                stage.Operation,
                FinancialAnalysisOperations.Signals,
                StringComparison.Ordinal))
            .ToArray();

        if (signalStages.Length != 1 ||
            signalStages[0].Status == FinancialAnalysisExecutionStatus.LegacyUnknown)
        {
            return Fallback(LegalCnvFallbackReasons.LegacyOrAmbiguousExecution);
        }

        if (signalStages[0].Status != FinancialAnalysisExecutionStatus.Succeeded)
        {
            return Fallback(LegalCnvFallbackReasons.SignalsStageFailed);
        }

        if (financialAnalysis.RiskSignals.Count == 0)
        {
            return Fallback(LegalCnvFallbackReasons.NoSpecificSignals);
        }

        return new LegalDataEvidenceContext(
            CanUseSignals: true,
            FallbackReason: null,
            DataToolStatus: dataToolStatus,
            FinancialAnalysisStatus: financialAnalysisStatus,
            FailedStages: failedStages
        );
    }

    private static IReadOnlyList<LegalDataStageFailureAudit> MapAllowlistedFailures(
        FinancialAnalysisContext? financialAnalysis)
    {
        if (financialAnalysis is null)
        {
            return [];
        }

        return financialAnalysis.Execution.Stages
            .Where(stage =>
                stage.Status == FinancialAnalysisExecutionStatus.Failed &&
                FinancialAnalysisOperations.All.Contains(
                    stage.Operation,
                    StringComparer.Ordinal))
            .Select(stage => new LegalDataStageFailureAudit(
                stage.Operation,
                SanitizeFailureCode(stage.FailureCode)))
            .ToArray();
    }

    private static string SanitizeFailureCode(string? failureCode)
    {
        return failureCode switch
        {
            FinancialAnalysisFailureCodes.PythonInvocationFailed =>
                FinancialAnalysisFailureCodes.PythonInvocationFailed,
            FinancialAnalysisFailureCodes.PythonResponseInvalid =>
                FinancialAnalysisFailureCodes.PythonResponseInvalid,
            FinancialAnalysisFailureCodes.UnexpectedFailure =>
                FinancialAnalysisFailureCodes.UnexpectedFailure,
            _ => FinancialAnalysisFailureCodes.UnexpectedFailure
        };
    }
}
