namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Curated regulatory-analysis quality case set.
/// </summary>
public sealed class AnalysisQualityCaseSet
{
    /// <summary>
    /// Gets the analysis quality cases.
    /// </summary>
    public required IReadOnlyList<AnalysisQualityCase> Cases { get; init; }
}
