namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Curated search-quality query set.
/// </summary>
public sealed class SearchQualityQuerySet
{
    /// <summary>
    /// Gets curated validation queries.
    /// </summary>
    public required IReadOnlyList<SearchQualityQuery> Queries { get; init; }
}
