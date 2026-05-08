namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Response returned by recent resolution lookup.
/// </summary>
public sealed class GetRecentResolutionsResponse
{
    /// <summary>
    /// Gets the recent mock resolution items.
    /// </summary>
    public required IReadOnlyList<RecentResolutionItem> Results { get; init; }

    /// <summary>
    /// Gets warnings that qualify the response.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
