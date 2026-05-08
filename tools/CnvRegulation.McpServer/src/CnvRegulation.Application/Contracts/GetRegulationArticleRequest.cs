namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Request for retrieving a structured article placeholder.
/// </summary>
public sealed class GetRegulationArticleRequest
{
    /// <summary>
    /// Gets the optional title filter.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the optional chapter filter.
    /// </summary>
    public string? Chapter { get; init; }

    /// <summary>
    /// Gets the optional section filter.
    /// </summary>
    public string? Section { get; init; }

    /// <summary>
    /// Gets the article label to retrieve.
    /// </summary>
    public required string Article { get; init; }
}
