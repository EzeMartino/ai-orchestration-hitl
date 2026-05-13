namespace CnvRegulation.Application.Contracts;

/// <summary>
/// One curated regulatory-analysis quality case.
/// </summary>
public sealed class AnalysisQualityCase
{
    /// <summary>
    /// Gets the stable case identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the text to analyze.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets the optional regulatory area.
    /// </summary>
    public string? RegulationArea { get; init; }

    /// <summary>
    /// Gets the expected analysis status.
    /// </summary>
    public required string ExpectedStatus { get; init; }

    /// <summary>
    /// Gets topics that should be detected.
    /// </summary>
    public IReadOnlyList<string> ExpectedTopics { get; init; } = [];

    /// <summary>
    /// Gets the minimum expected finding count.
    /// </summary>
    public int MinFindings { get; init; }

    /// <summary>
    /// Gets the minimum expected citation count.
    /// </summary>
    public int MinCitations { get; init; }
}
