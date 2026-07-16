namespace Orchestration.Application.Agents.Legal.Cnv;

public static class LegalCnvQuerySources
{
    public const string Contextual = "contextual";
    public const string Fallback = "fallback";
}

public sealed record LegalCnvQueryPlan
{
    public LegalCnvQueryPlan(
        string StrategyVersion,
        string Source,
        string? FallbackReason,
        LegalDataEvidenceContext DataEvidence,
        IReadOnlyList<LegalCnvQuery> Queries)
    {
        ArgumentNullException.ThrowIfNull(DataEvidence);
        ArgumentNullException.ThrowIfNull(DataEvidence.FailedStages);
        ArgumentNullException.ThrowIfNull(Queries);

        this.StrategyVersion = StrategyVersion;
        this.Source = Source;
        this.FallbackReason = FallbackReason;
        this.DataEvidence = DataEvidence with
        {
            FailedStages = Array.AsReadOnly(DataEvidence.FailedStages.ToArray())
        };
        this.Queries = Array.AsReadOnly(Queries.ToArray());
    }

    public string StrategyVersion { get; }

    public string Source { get; }

    public string? FallbackReason { get; }

    public LegalDataEvidenceContext DataEvidence { get; }

    public IReadOnlyList<LegalCnvQuery> Queries { get; }
}
