namespace Orchestration.Application.Agents.Legal.Cnv;

public static class LegalCnvQuerySources
{
    public const string Contextual = "contextual";
    public const string Fallback = "fallback";
}

public sealed record LegalCnvQueryPlan(
    string StrategyVersion,
    string Source,
    string? FallbackReason,
    LegalDataEvidenceContext DataEvidence,
    IReadOnlyList<LegalCnvQuery> Queries
);
