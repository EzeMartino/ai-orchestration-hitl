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
    IReadOnlyList<LegalCnvQueryAudit> Queries,
    IReadOnlyList<LegalCnvEnrichmentAudit>? Enrichments = null
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

public sealed record LegalCnvEnrichmentAudit(
    string EnrichmentId,
    int Rank,
    string CandidateKey,
    double Score,
    IReadOnlyList<int> ContributingQueryIndices,
    LegalCnvEnrichmentStageAudit Document,
    LegalCnvEnrichmentStageAudit Article,
    string Status,
    IReadOnlyList<string> LimitationCodes);

public sealed record LegalCnvEnrichmentStageAudit(
    bool Selected,
    bool Attempted,
    bool FromCache,
    string Status,
    int? OriginalTextLength,
    bool IsTruncated);

public static class LegalCnvEnrichmentStageStatuses
{
    public const string NotApplicable = "NotApplicable";
    public const string NotAttempted = "NotAttempted";
    public const string Succeeded = "Succeeded";
    public const string Missing = "Missing";
    public const string TimedOut = "TimedOut";
    public const string Malformed = "Malformed";
    public const string Failed = "Failed";
    public const string Conflict = "Conflict";
}
