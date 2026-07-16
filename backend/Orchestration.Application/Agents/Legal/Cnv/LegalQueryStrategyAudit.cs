using System.Collections.Generic;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Application.Agents.Legal.Cnv;

public sealed record LegalQueryStrategyAudit(
    string StrategyVersion,
    string Source,
    string? FallbackReason,
    string DataToolStatus,
    FinancialAnalysisExecutionStatus? FinancialAnalysisStatus,
    IReadOnlyList<LegalDataStageFailureAudit> FailedStages,
    IReadOnlyList<LegalCnvQueryAudit> Queries
);

public sealed record LegalCnvQueryAudit(
    int Index,
    int Total,
    string Query,
    string? RegulationArea,
    string Reason,
    IReadOnlyList<string> RelatedFinancialSignals,
    string ExecutionStatus,
    int ResultCount,
    int CitedEvidenceCount
);

public static class LegalCnvQueryExecutionStatuses
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}
