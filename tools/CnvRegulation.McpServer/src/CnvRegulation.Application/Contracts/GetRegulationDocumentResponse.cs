using CnvRegulation.Domain;

namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Response returned when retrieving a regulatory document.
/// </summary>
public sealed class GetRegulationDocumentResponse
{
    /// <summary>
    /// Gets the document metadata and content.
    /// </summary>
    public required RegulationDocument Document { get; init; }

    /// <summary>
    /// Gets citations for the returned document.
    /// </summary>
    public required IReadOnlyList<RegulationCitation> Citations { get; init; }

    /// <summary>
    /// Gets warnings that qualify the response.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
