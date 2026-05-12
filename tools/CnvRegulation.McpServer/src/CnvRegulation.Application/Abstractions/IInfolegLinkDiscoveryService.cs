using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Discovers controlled official Infoleg links from local source files.
/// </summary>
public interface IInfolegLinkDiscoveryService
{
    /// <summary>
    /// Discovers official Infoleg links and writes a candidate source manifest.
    /// </summary>
    /// <param name="request">The discovery request.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The discovery response.</returns>
    Task<InfolegLinkDiscoveryResponse> DiscoverAsync(
        InfolegLinkDiscoveryRequest request,
        CancellationToken cancellationToken);
}
