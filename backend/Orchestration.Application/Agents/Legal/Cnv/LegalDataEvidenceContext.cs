using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Application.Agents.Legal.Cnv;

public static class LegalDataToolStatuses
{
    public const string Executed = "executed";
    public const string Failed = "failed";
    public const string Absent = "absent";
    public const string Unknown = "unknown";
}

public static class LegalCnvFallbackReasons
{
    public const string DataToolFailed = "data_tool_failed";
    public const string DataStageAbsent = "data_stage_absent";
    public const string FinancialAnalysisMissing = "financial_analysis_missing";
    public const string SignalsStageFailed = "signals_stage_failed";
    public const string NoSpecificSignals = "no_specific_signals";
    public const string SignalsUnmapped = "signals_unmapped";
    public const string LegacyOrAmbiguousExecution = "legacy_or_ambiguous_execution";
}

public sealed record LegalDataStageFailureAudit(
    string Operation,
    string FailureCode
);

public sealed record LegalDataEvidenceContext(
    bool CanUseSignals,
    string? FallbackReason,
    string DataToolStatus,
    FinancialAnalysisExecutionStatus? FinancialAnalysisStatus,
    IReadOnlyList<LegalDataStageFailureAudit> FailedStages
);
