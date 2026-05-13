namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Per-case regulatory-analysis quality validation result.
/// </summary>
public sealed class AnalysisQualityValidationResult
{
    /// <summary>
    /// Gets the case identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the analyzed text.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets the optional regulatory area.
    /// </summary>
    public string? RegulationArea { get; init; }

    /// <summary>
    /// Gets the expected status.
    /// </summary>
    public required string ExpectedStatus { get; init; }

    /// <summary>
    /// Gets the actual status.
    /// </summary>
    public required string ActualStatus { get; init; }

    /// <summary>
    /// Gets expected topics.
    /// </summary>
    public required IReadOnlyList<string> ExpectedTopics { get; init; }

    /// <summary>
    /// Gets detected topic names.
    /// </summary>
    public required IReadOnlyList<string> DetectedTopics { get; init; }

    /// <summary>
    /// Gets finding count.
    /// </summary>
    public int FindingsCount { get; init; }

    /// <summary>
    /// Gets citation count.
    /// </summary>
    public int CitationsCount { get; init; }

    /// <summary>
    /// Gets distinct risk levels.
    /// </summary>
    public required IReadOnlyList<string> RiskLevels { get; init; }

    /// <summary>
    /// Gets analysis warnings.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>
    /// Gets whether this case passed expectations.
    /// </summary>
    public bool Passed { get; init; }

    /// <summary>
    /// Gets failure reasons.
    /// </summary>
    public required IReadOnlyList<string> FailureReasons { get; init; }
}
