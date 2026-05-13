namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Request for compliance analysis against CNV material.
/// </summary>
public sealed class AnalyzeTextAgainstCnvRequest
{
    /// <summary>
    /// Gets the text to analyze.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets the optional regulatory area.
    /// </summary>
    public string? RegulationArea { get; init; }

    /// <summary>
    /// Gets whether strict checks should prefer full-text evidence.
    /// </summary>
    public bool StrictMode { get; init; } = true;

    /// <summary>
    /// Gets whether hybrid search may be used as secondary exploratory evidence.
    /// </summary>
    public bool UseHybridSearch { get; init; }
}
