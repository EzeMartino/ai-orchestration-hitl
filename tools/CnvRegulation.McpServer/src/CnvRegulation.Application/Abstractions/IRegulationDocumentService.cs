using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Retrieves CNV regulatory documents.
/// </summary>
public interface IRegulationDocumentService
{
    /// <summary>
    /// Retrieves a regulatory document by identifier.
    /// </summary>
    /// <param name="request">The document request.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The document response.</returns>
    Task<GetRegulationDocumentResponse> GetDocumentAsync(
        GetRegulationDocumentRequest request,
        CancellationToken cancellationToken);
}
