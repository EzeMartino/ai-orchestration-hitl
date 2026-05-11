using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Application.Abstractions;

/// <summary>
/// Inspects stored regulation chunks for quality issues.
/// </summary>
public interface IChunkQualityInspectionService
{
    /// <summary>
    /// Creates a quality report for currently stored chunks.
    /// </summary>
    /// <param name="request">The inspection request.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The chunk quality report.</returns>
    Task<ChunkQualityReport> InspectAsync(
        InspectChunksRequest request,
        CancellationToken cancellationToken);
}
