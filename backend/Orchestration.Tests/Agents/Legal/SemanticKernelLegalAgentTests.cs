using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Shared;
using Orchestration.Infrastructure.Agents.Legal;

namespace Orchestration.Tests.Agents.Legal;

public class SemanticKernelLegalAgentTests
{
    [Fact]
    public async Task ReviewAsync_Should_return_compliance_risk_using_semantic_kernel_plugin()
    {
        var services = new ServiceCollection();

        services.AddScoped<LegalCompliancePlugin>();
        services.AddScoped<ILegalAgent, SemanticKernelLegalAgent>();

        await using var serviceProvider = services.BuildServiceProvider();

        var agent = serviceProvider.GetRequiredService<ILegalAgent>();

        var report = new FinancialReportContext(
            SessionId: Guid.NewGuid(),
            ReportName: "legal-agent-test-report",
            TotalAmount: 125000m,
            TransactionCount: 42,
            SubmittedAt: DateTimeOffset.UtcNow
        );

        var result = await agent.ReviewAsync(
            report,
            CancellationToken.None
        );

        result.Engine.Should().Be("Semantic Kernel + Mock Compliance Knowledge Base");
        result.HasComplianceRisk.Should().BeTrue();
        result.RiskLevel.Should().Be("Medium");
        result.Evidence.Should().NotBeEmpty();

        result.Evidence
            .Should()
            .Contain(x => x.Regulation == "Internal AML Policy");
    }
}