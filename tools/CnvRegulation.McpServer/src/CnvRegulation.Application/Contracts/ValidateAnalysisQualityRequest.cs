namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Request for regulatory-analysis quality validation.
/// </summary>
public sealed class ValidateAnalysisQualityRequest
{
    /// <summary>
    /// Gets the analysis quality case set JSON path.
    /// </summary>
    public required string CaseSetPath { get; init; }

    /// <summary>
    /// Gets whether hybrid search may be used as secondary exploratory evidence.
    /// </summary>
    public bool UseHybridSearch { get; init; }

    /// <summary>
    /// Gets whether strict analysis mode should be used.
    /// </summary>
    public bool StrictMode { get; init; } = true;
}
