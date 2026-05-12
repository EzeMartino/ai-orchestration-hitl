namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Diagnostic count for one generated search query.
/// </summary>
public sealed class GeneratedSearchQueryDiagnostic
{
    /// <summary>
    /// Gets the generated search query.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Gets the raw result count observed for the query.
    /// </summary>
    public int RawResultCount { get; init; }
}
