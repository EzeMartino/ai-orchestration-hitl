using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Application.Agents.Legal.Cnv;

public static class LegalDataEvidenceClassifier
{
    public static LegalDataEvidenceContext Classify(
        string dataToolStatus,
        FinancialAnalysisContext? financialAnalysis)
    {
        var safeDataToolStatus = dataToolStatus switch
        {
            LegalDataToolStatuses.Executed => LegalDataToolStatuses.Executed,
            LegalDataToolStatuses.Failed => LegalDataToolStatuses.Failed,
            LegalDataToolStatuses.Absent => LegalDataToolStatuses.Absent,
            _ => LegalDataToolStatuses.Unknown
        };
        var financialAnalysisStatus = financialAnalysis?.Execution.OverallStatus;
        var failedStages = MapAllowlistedFailures(financialAnalysis);

        LegalDataEvidenceContext Fallback(string reason)
        {
            return new LegalDataEvidenceContext(
                CanUseSignals: false,
                FallbackReason: reason,
                DataToolStatus: safeDataToolStatus,
                FinancialAnalysisStatus: financialAnalysisStatus,
                FailedStages: failedStages
            );
        }

        if (string.Equals(
                safeDataToolStatus,
                LegalDataToolStatuses.Failed,
                StringComparison.Ordinal))
        {
            return Fallback(LegalCnvFallbackReasons.DataToolFailed);
        }

        if (string.Equals(
                safeDataToolStatus,
                LegalDataToolStatuses.Absent,
                StringComparison.Ordinal))
        {
            return Fallback(LegalCnvFallbackReasons.DataStageAbsent);
        }

        if (safeDataToolStatus != LegalDataToolStatuses.Executed)
        {
            return Fallback(LegalCnvFallbackReasons.LegacyOrAmbiguousExecution);
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

        if (financialAnalysis.Execution.OverallStatus ==
            FinancialAnalysisExecutionStatus.Failed)
        {
            return Fallback(LegalCnvFallbackReasons.LegacyOrAmbiguousExecution);
        }

        if (financialAnalysis.RiskSignals.Count == 0)
        {
            return Fallback(LegalCnvFallbackReasons.NoSpecificSignals);
        }

        return new LegalDataEvidenceContext(
            CanUseSignals: true,
            FallbackReason: null,
            DataToolStatus: safeDataToolStatus,
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

        var failedStages = financialAnalysis.Execution.Stages
            .Where(stage => stage.Status == FinancialAnalysisExecutionStatus.Failed)
            .ToArray();
        var audits = new List<LegalDataStageFailureAudit>();

        foreach (var operation in FinancialAnalysisOperations.All)
        {
            var failureCodes = failedStages
                .Where(stage => string.Equals(
                    stage.Operation,
                    operation,
                    StringComparison.Ordinal))
                .Select(stage => SanitizeFailureCode(stage.FailureCode))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (failureCodes.Length == 0)
            {
                continue;
            }

            audits.Add(new LegalDataStageFailureAudit(
                operation,
                failureCodes.Length == 1
                    ? failureCodes[0]
                    : FinancialAnalysisFailureCodes.UnexpectedFailure
            ));
        }

        return audits.ToArray();
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
