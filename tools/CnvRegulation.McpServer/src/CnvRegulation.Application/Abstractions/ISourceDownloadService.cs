using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Downloads regulatory source files from a controlled manifest.
/// </summary>
public interface ISourceDownloadService
{
    /// <summary>
    /// Downloads source files and writes sidecar metadata files.
    /// </summary>
    /// <param name="request">The download request.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The download response.</returns>
    Task<DownloadSourcesResponse> DownloadAsync(
        DownloadSourcesRequest request,
        CancellationToken cancellationToken);
}
