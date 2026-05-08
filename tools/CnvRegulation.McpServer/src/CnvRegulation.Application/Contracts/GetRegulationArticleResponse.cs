using CnvRegulation.Domain;

namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Response returned when retrieving a regulatory article.
/// </summary>
public sealed class GetRegulationArticleResponse
{
    /// <summary>
    /// Gets the article text or mock placeholder text.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets the citation for the returned text.
    /// </summary>
    public required RegulationCitation Citation { get; init; }

    /// <summary>
    /// Gets the mock confidence score.
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Gets warnings that qualify the response.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
