using CnvRegulation.Application.Contracts;
using CnvRegulation.Infrastructure.InMemory;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class MockComplianceAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_ShouldReturnDisclaimerAndFindings()
    {
        var service = new MockComplianceAnalysisService();
        var request = new AnalyzeTextAgainstCnvRequest
        {
            Text = "El agente podra ofrecer instrumentos...",
            RegulationArea = "Agentes",
            StrictMode = true
        };

        var response = await service.AnalyzeAsync(request, CancellationToken.None);

        response.Status.Should().Be("requires_review");
        response.Disclaimer.Should().NotBeNullOrWhiteSpace();
        response.Findings.Should().NotBeEmpty();

        var finding = response.Findings[0];
        finding.RiskLevel.Should().NotBeNullOrWhiteSpace();
        finding.Citation.Should().NotBeNull();
        finding.Citation.Url.Should().NotBeNullOrWhiteSpace();
        finding.Citation.QuotedText.Should().Contain("placeholder");
        response.Warnings.Should().Contain(warning => warning.Contains("Mock data", StringComparison.OrdinalIgnoreCase));
    }
}
