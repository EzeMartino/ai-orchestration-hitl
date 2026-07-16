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

        var querySnapshots = new LegalCnvQuery[Queries.Count];
        for (var index = 0; index < Queries.Count; index++)
        {
            var query = Queries[index];
            if (query is null)
            {
                throw new ArgumentException(
                    $"Queries cannot contain a null query (index {index}).",
                    nameof(Queries)
                );
            }

            if (query.RelatedFinancialSignals is null)
            {
                throw new ArgumentException(
                    $"Queries cannot contain a query with null RelatedFinancialSignals (index {index}).",
                    nameof(Queries)
                );
            }

            querySnapshots[index] = query with
            {
                RelatedFinancialSignals = Array.AsReadOnly(
                    query.RelatedFinancialSignals.ToArray())
            };
        }

        this.Queries = Array.AsReadOnly(querySnapshots);
    }

    public string StrategyVersion { get; }

    public string Source { get; }

    public string? FallbackReason { get; }

    public LegalDataEvidenceContext DataEvidence { get; }

    public IReadOnlyList<LegalCnvQuery> Queries { get; }
}
