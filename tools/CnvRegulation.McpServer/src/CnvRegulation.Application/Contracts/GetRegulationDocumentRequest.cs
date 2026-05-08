namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Request for retrieving a regulatory document by identifier.
/// </summary>
public sealed class GetRegulationDocumentRequest
{
    /// <summary>
    /// Gets the stable document identifier.
    /// </summary>
    public required string DocumentId { get; init; }
}
