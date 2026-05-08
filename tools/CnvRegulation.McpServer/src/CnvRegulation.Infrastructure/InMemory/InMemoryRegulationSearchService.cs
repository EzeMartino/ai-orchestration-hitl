using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Infrastructure.InMemory;

/// <summary>
/// Mock in-memory search implementation for the first CNV MCP MVP.
/// </summary>
public sealed class InMemoryRegulationSearchService : IRegulationSearchService
{
    /// <inheritdoc />
    public Task<SearchRegulationResponse> SearchAsync(
        SearchRegulationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var limit = request.Limit <= 0 ? 5 : Math.Min(request.Limit, 25);
        var query = string.IsNullOrWhiteSpace(request.Query) ? string.Empty : request.Query.Trim();

        var results = MockRegulationData.SearchResults
            .Where(result => MatchesArea(result, request.Area))
            .Take(limit)
            .ToArray();

        return Task.FromResult(new SearchRegulationResponse
        {
            Query = query,
            Results = results,
            Warnings = [MockRegulationData.MockWarning]
        });
    }

    private static bool MatchesArea(CnvRegulation.Domain.RegulationSearchResult result, string? area)
    {
        if (string.IsNullOrWhiteSpace(area))
        {
            return true;
        }

        return result.Snippet.Contains(area, StringComparison.OrdinalIgnoreCase)
            || result.Title.Contains(area, StringComparison.OrdinalIgnoreCase)
            || string.Equals(area, "Agentes", StringComparison.OrdinalIgnoreCase);
    }
}
