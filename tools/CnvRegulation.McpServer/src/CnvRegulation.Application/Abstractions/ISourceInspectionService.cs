using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Inspects local regulatory source files and reports parsing/chunking diagnostics.
/// </summary>
public interface ISourceInspectionService
{
    /// <summary>
    /// Inspects regulatory source files in a local directory.
    /// </summary>
    /// <param name="request">The inspection request.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The inspection response.</returns>
    Task<InspectSourcesResponse> InspectAsync(
        InspectSourcesRequest request,
        CancellationToken cancellationToken);
}
