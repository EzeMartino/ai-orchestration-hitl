using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Ingests local regulatory source files into the document repository.
/// </summary>
public interface IRegulationIngestionService
{
    /// <summary>
    /// Ingests regulatory source files from a local directory.
    /// </summary>
    /// <param name="request">The ingestion request.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The ingestion response.</returns>
    Task<IngestRegulationSourceResponse> IngestAsync(
        IngestRegulationSourceRequest request,
        CancellationToken cancellationToken);
}
