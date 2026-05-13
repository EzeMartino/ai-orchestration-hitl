namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Response returned by CNV compliance analysis.
/// </summary>
public sealed class AnalyzeTextAgainstCnvResponse
{
    /// <summary>
    /// Gets the analysis status.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Gets findings found during analysis.
    /// </summary>
    public required IReadOnlyList<ComplianceFinding> Findings { get; init; }

    /// <summary>
    /// Gets warnings that qualify the response.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>
    /// Gets the legal disclaimer for the response.
    /// </summary>
    public required string Disclaimer { get; init; }
}
