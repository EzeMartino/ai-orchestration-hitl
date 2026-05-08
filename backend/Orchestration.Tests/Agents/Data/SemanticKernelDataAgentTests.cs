using FluentAssertions;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Tests.Agents.Data;

[Collection(PythonAgentTestCollection.CollectionName)]
public class SemanticKernelDataAgentTests
{
    private readonly PythonAgentTestFixture _fixture;

    public SemanticKernelDataAgentTests(PythonAgentTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AnalyzeAsync_Should_return_result_using_semantic_kernel_and_python_csnakes()
    {
        var agent = _fixture.GetRequiredService<IDataAgent>();

        var report = new FinancialReportContext(
            SessionId: Guid.NewGuid(),
            ReportName: "semantic-kernel-test-report",
            TotalAmount: 125000m,
            TransactionCount: 42,
            SubmittedAt: DateTimeOffset.UtcNow
        );

        var result = await agent.AnalyzeAsync(
            report,
            CancellationToken.None
        );

        result.Engine.Should().Be("Semantic Kernel + Python/CSnakes");
        result.HasAnomaly.Should().BeTrue();
        result.Severity.Should().Be("High");
        result.Evidence.Should().HaveCount(2);
        result.Evidence.Should().Contain(x => x.Metric == "TransactionAmountZScore");
        result.Evidence.Should().Contain(x => x.Metric == "VelocityScore");
    }
}
