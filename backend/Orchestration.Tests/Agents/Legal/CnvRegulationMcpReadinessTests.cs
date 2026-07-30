using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Application.Agents.Shared;
using Orchestration.Infrastructure.Agents.Legal.Regulations;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

namespace Orchestration.Tests.Agents.Legal;

public sealed class CnvRegulationMcpReadinessTests
{
    [Fact]
    public async Task ProbeAsync_Should_issue_only_the_fixed_read_only_search_and_accept_zero_hits()
    {
        var client = new ProbeClient(new CnvRegulationSearchResponse("CNV", [], []));
        var probe = new CnvRegulationMcpProbe(client);

        await probe.ProbeAsync(CancellationToken.None);

        client.Requests.Should().ContainSingle().Which.Should().Be(
            new CnvRegulationSearchRequest("CNV", Limit: 1));
    }

    [Fact]
    public async Task ProbeAsync_Should_accept_one_hit()
    {
        var client = new ProbeClient(new CnvRegulationSearchResponse("CNV", [new CnvRegulationSearchResult("document", "chunk", "title", null, null, null, "CNV", null, "snippet", 1d, [])], []));
        var probe = new CnvRegulationMcpProbe(client);

        await probe.ProbeAsync(CancellationToken.None);

        client.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ProbeAsync_Should_propagate_caller_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var probe = new CnvRegulationMcpProbe(new ProbeClient());

        var action = () => probe.ProbeAsync(cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task HealthCheck_Should_be_healthy_when_optional_mcp_is_disabled_without_probing()
    {
        var probe = new RecordingProbe();
        var healthCheck = new CnvRegulationMcpHealthCheck(
            Options.Create(new CnvRegulationMcpOptions { Enabled = false }), probe);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
        probe.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(IOException))]
    public async Task HealthCheck_Should_return_safe_unhealthy_result_without_exception_details(Type exceptionType)
    {
        var probe = new RecordingProbe((Exception)Activator.CreateInstance(exceptionType)!);
        var healthCheck = new CnvRegulationMcpHealthCheck(
            Options.Create(new CnvRegulationMcpOptions { Enabled = true, Args = ["run"] }), probe);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("CNV MCP readiness probe failed.");
        result.Description.Should().NotContain(exceptionType.Name);
    }

    [Fact]
    public async Task HealthCheck_Should_propagate_caller_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var healthCheck = new CnvRegulationMcpHealthCheck(
            Options.Create(new CnvRegulationMcpOptions { Enabled = true, Args = ["run"] }),
            new RecordingProbe());

        var action = () => healthCheck.CheckHealthAsync(new HealthCheckContext(), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task StartupService_Should_fail_required_startup_without_leaking_probe_details()
    {
        var startup = new CnvRegulationMcpStartupService(
            Options.Create(new CnvRegulationMcpOptions { Enabled = true, Required = true, Args = ["run"] }),
            new RecordingProbe(new IOException("transport detail")));

        var action = () => startup.StartAsync(CancellationToken.None);

        var failure = await action.Should().ThrowAsync<InvalidOperationException>();
        failure.Which.Message.Should().Be("CNV MCP readiness probe failed.");
        failure.Which.Message.Should().NotContain("transport detail");
    }

    [Fact]
    public async Task StartupService_Should_skip_optional_mcp()
    {
        var probe = new RecordingProbe();
        var startup = new CnvRegulationMcpStartupService(
            Options.Create(new CnvRegulationMcpOptions { Enabled = true, Required = false, Args = ["run"] }),
            probe);

        await startup.StartAsync(CancellationToken.None);

        probe.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task UnavailableSource_Should_fail_closed_without_findings_or_citations()
    {
        var source = new UnavailableRegulatoryKnowledgeSource();
        var report = new FinancialReportContext(Guid.NewGuid(), "report", 0m, 0, DateTimeOffset.UtcNow);

        var result = await source.ReviewAsync(report, CancellationToken.None);

        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("NotEstablished");
        result.Findings.Should().BeEmpty();
        result.RequiresHumanReview.Should().BeTrue();
        result.EvidenceAssessment!.EvidenceFound.Should().BeFalse();
        result.EvidenceAssessment.RequiresHumanReview.Should().BeTrue();
    }

    private sealed class RecordingProbe(Exception? exception = null) : ICnvRegulationMcpProbe
    {
        public int CallCount { get; private set; }

        public Task ProbeAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return exception is null ? Task.CompletedTask : Task.FromException(exception);
        }
    }

    private sealed class ProbeClient(CnvRegulationSearchResponse? response = null) : ICnvRegulationMcpClient
    {
        public List<CnvRegulationSearchRequest> Requests { get; } = [];
        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;

        public Task<CnvRegulationSearchResponse> SearchAsync(CnvRegulationSearchRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(response ?? new CnvRegulationSearchResponse("CNV", [], []));
        }

        public Task<CnvRegulationDocumentResponse> GetDocumentAsync(CnvRegulationDocumentRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CnvRegulationArticleResponse> GetArticleAsync(CnvRegulationArticleRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
