using CnvRegulation.Domain;

namespace CnvRegulation.Application.Contracts;

/// <summary>
/// Represents one mock compliance analysis finding.
/// </summary>
public sealed class ComplianceFinding
{
    /// <summary>
    /// Gets the finding risk level.
    /// </summary>
    public required string RiskLevel { get; init; }

    /// <summary>
    /// Gets the issue detected by the mock analysis.
    /// </summary>
    public required string Issue { get; init; }

    /// <summary>
    /// Gets the citation supporting the finding.
    /// </summary>
    public required RegulationCitation Citation { get; init; }

    /// <summary>
    /// Gets a short reasoning summary.
    /// </summary>
    public required string ReasoningSummary { get; init; }

    /// <summary>
    /// Gets the mock confidence score.
    /// </summary>
    public double Confidence { get; init; }
}
