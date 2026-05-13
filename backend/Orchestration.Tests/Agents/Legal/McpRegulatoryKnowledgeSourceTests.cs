using FluentAssertions;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Shared;
using Orchestration.Infrastructure.Agents.Legal.Regulations;
using Orchestration.Tests.Agents.Legal;

namespace Orchestration.Tests.Agents.Legal;

public class McpRegulatoryKnowledgeSourceTests
{
    [Fact]
    public async Task ReviewAsync_Should_map_cited_mcp_results_to_regulatory_findings()
    {
        var client = new FakeCnvRegulationMcpClient();

        var options = Options.Create(
            new CnvRegulationMcpOptions
            {
                Enabled = true,
                Command = "dotnet",
                Args = [],
                DefaultLimit = 5
            }
        );

        var source = new McpRegulatoryKnowledgeSource(
            client,
            options
        );

        var report = new FinancialReportContext(
            SessionId: Guid.NewGuid(),
            ReportName: "cnv-mcp-test-report",
            TotalAmount: 125000m,
            TransactionCount: 42,
            SubmittedAt: DateTimeOffset.UtcNow
        );

        var result = await source.ReviewAsync(
            report,
            CancellationToken.None
        );

        result.SourceEngine.Should().Be("MCP CNV Regulation Server");
        result.HasComplianceRisk.Should().BeTrue();
        result.RiskLevel.Should().Be("Medium");
        result.Findings.Should().NotBeEmpty();
        result.Warnings.Should().Contain(
            "Automated regulatory retrieval only. Human legal review is required before making operational decisions."
        );

        result.Findings
            .Should()
            .Contain(x =>
                x.Regulation.Contains("622/2013") &&
                x.Section == "Articulo 1" &&
                x.Finding == "Texto normativo citado."
            );
    }
}
