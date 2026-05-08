using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Infrastructure.InMemory;

/// <summary>
/// Mock in-memory recent resolution implementation for the first CNV MCP MVP.
/// </summary>
public sealed class InMemoryRecentResolutionService : IRecentResolutionService
{
    /// <inheritdoc />
    public Task<GetRecentResolutionsResponse> GetRecentAsync(
        GetRecentResolutionsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var days = request.Days <= 0 ? 30 : Math.Min(request.Days, 365);
        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-days);

        var results = MockRegulationData.RecentResolutions
            .Where(item => item.PublicationDate is null || item.PublicationDate >= from)
            .Where(item => MatchesSource(item, request.Source))
            .OrderByDescending(item => item.PublicationDate)
            .ToArray();

        return Task.FromResult(new GetRecentResolutionsResponse
        {
            Results = results,
            Warnings = [MockRegulationData.MockWarning]
        });
    }

    private static bool MatchesSource(RecentResolutionItem item, string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return true;
        }

        return string.Equals(item.Source, source.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
