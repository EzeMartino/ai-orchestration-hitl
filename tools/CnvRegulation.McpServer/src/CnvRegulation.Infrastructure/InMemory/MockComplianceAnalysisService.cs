using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Infrastructure.InMemory;

/// <summary>
/// Mock compliance analyzer for the first CNV MCP MVP.
/// </summary>
public sealed class MockComplianceAnalysisService : IComplianceAnalysisService
{
    /// <inheritdoc />
    public Task<AnalyzeTextAgainstCnvResponse> AnalyzeAsync(
        AnalyzeTextAgainstCnvRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var findingCitation = MockRegulationData.CreateCitation(
            "Normas CNV N.T. 2013",
            "Normas CNV",
            null,
            "Titulo mock",
            "Seccion mock",
            "Articulo mock",
            "Mock regulatory text used as a placeholder citation. This is not official CNV text.");

        var findings = string.IsNullOrWhiteSpace(request.Text)
            ? Array.Empty<ComplianceFinding>()
            :
            [
                new ComplianceFinding
                {
                    RiskLevel = "medium",
                    Issue = "Potential regulatory issue detected in mock analysis.",
                    Citation = findingCitation,
                    ReasoningSummary = "The text may require validation against CNV rules.",
                    Confidence = request.StrictMode ? 0.65 : 0.5
                }
            ];

        return Task.FromResult(new AnalyzeTextAgainstCnvResponse
        {
            Status = findings.Length > 0 ? "requires_review" : "no_findings",
            Findings = findings,
            Warnings = [MockRegulationData.MockWarning],
            Disclaimer = MockRegulationData.Disclaimer
        });
    }
}
