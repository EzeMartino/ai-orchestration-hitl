using CnvRegulation.Domain;

namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Response returned when retrieving a regulatory article.
/// </summary>
public sealed class GetRegulationArticleResponse
{
    /// <summary>
    /// Gets whether the requested article was found in the configured repository.
    /// </summary>
    public required bool Found { get; init; }

    /// <summary>
    /// Gets the article text when found.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Gets the citation for the returned text when found.
    /// </summary>
    public RegulationCitation? Citation { get; init; }

    /// <summary>
    /// Gets the confidence score for the returned article.
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Gets warnings that qualify the response.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
