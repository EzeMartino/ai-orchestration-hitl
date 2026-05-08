using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Creates controlled source manifests for regulatory downloads.
/// </summary>
public interface ISourceDiscoveryService
{
    /// <summary>
    /// Creates a source manifest file.
    /// </summary>
    /// <param name="request">The discovery request.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The discovery response.</returns>
    Task<DiscoverSourcesResponse> DiscoverAsync(
        DiscoverSourcesRequest request,
        CancellationToken cancellationToken);
}
