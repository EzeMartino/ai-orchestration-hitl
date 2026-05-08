namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Request for inspecting downloaded/local source files.
/// </summary>
public sealed class InspectSourcesRequest
{
    /// <summary>
    /// Gets the directory containing source files and metadata sidecars.
    /// </summary>
    public required string SourceDirectory { get; init; }
}
