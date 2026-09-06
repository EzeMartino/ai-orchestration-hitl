using FluentAssertions;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Shared;
using Orchestration.Infrastructure.Agents.Data;

namespace Orchestration.Tests.Agents.Data;

[Collection(PythonAgentTestCollection.CollectionName)]
public class CSnakesDataAgentTests
{
    private readonly PythonAgentTestFixture _fixture;

    public CSnakesDataAgentTests(PythonAgentTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AnalyzeAsync_CanceledRequest_DoesNotRunLegacyAnalysis()
    {
        var agent = _fixture.GetRequiredService<IDataAgent>();
        var report = new FinancialReportContext(
            Guid.NewGuid(), "canceled-report", 125000m, 42, DateTimeOffset.UtcNow);

        var analyze = async () => await agent.AnalyzeAsync(report, new CancellationToken(true));

        await analyze.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AnalyzeAsync_Should_return_python_generated_anomaly_result()
    {
        var agent = _fixture.GetRequiredService<IDataAgent>();

        var report = new FinancialReportContext(
            SessionId: Guid.NewGuid(),
            ReportName: "test-report",
            TotalAmount: 125000m,
            TransactionCount: 42,
            SubmittedAt: DateTimeOffset.UtcNow
        );

        var result = await agent.AnalyzeAsync(
            report,
            CancellationToken.None
        );

        result.Engine.Should().Be("Python/CSnakes");
        result.HasAnomaly.Should().BeTrue();
        result.Severity.Should().Be("High");
        result.Evidence.Should().HaveCount(2);

        result.Evidence
            .Should()
            .Contain(x =>
                x.Metric == "TransactionAmountZScore" &&
                x.Value > x.Threshold
            );

        result.Evidence
            .Should()
            .Contain(x =>
                x.Metric == "VelocityScore" &&
                x.Value > x.Threshold
            );
    }
}
