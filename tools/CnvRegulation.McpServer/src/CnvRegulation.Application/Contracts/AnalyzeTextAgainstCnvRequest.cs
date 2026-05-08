namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Request for mock compliance analysis against CNV material.
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
    /// Gets whether strict mock checks should be applied.
    /// </summary>
    public bool StrictMode { get; init; } = true;
}
