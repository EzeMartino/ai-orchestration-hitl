using CnvRegulation.Domain;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Stores and retrieves regulation documents for the MCP server.
/// </summary>
public interface IRegulationRepository
{
    /// <summary>
    /// Saves or replaces a regulatory document.
    /// </summary>
    /// <param name="document">The document to save.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>A task that completes when the document is saved.</returns>
    Task SaveAsync(RegulationDocument document, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a regulatory document by identifier.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The document when found; otherwise, null.</returns>
    Task<RegulationDocument?> GetByIdAsync(string documentId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists all documents currently stored in the repository.
    /// </summary>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The stored documents.</returns>
    Task<IReadOnlyList<RegulationDocument>> ListAsync(CancellationToken cancellationToken);
}
