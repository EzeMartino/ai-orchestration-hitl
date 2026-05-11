using CnvRegulation.Application.Search;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Expands user-entered regulation search queries with controlled aliases and synonyms.
/// </summary>
public interface IRegulationQueryExpander
{
    /// <summary>
    /// Expands a query for search.
    /// </summary>
    /// <param name="query">The original user query.</param>
    /// <returns>The query expansion result.</returns>
    RegulationQueryExpansion Expand(string query);
}
