using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Shared;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsProviderTests
{
    [Fact]
    public async Task Session_provider_Should_read_metrics_from_session_context()
    {
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var session = AnalysisSession.Create();
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var service = StructuredFinancialMetricsSessionServiceTests.CreateService(dbContext);
        await service.SaveAsync(
            session.Id,
            StructuredFinancialMetricsSessionServiceTests.CreateInput(),
            CancellationToken.None
        );
        var provider = new SessionStructuredFinancialMetricsProvider(service);

        var document = await provider.GetMetricsAsync(
            CreateReport(session.Id),
            CancellationToken.None
        );

        document.Should().NotBeNull();
        document!.DocumentId.Should().Be("vista-energy-structured-input");
        document.Metrics.Should().ContainSingle(metric => metric.Name == "revenue");
    }

    [Fact]
    public async Task Session_provider_Should_return_null_when_no_metrics_exist()
    {
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var service = StructuredFinancialMetricsSessionServiceTests.CreateService(dbContext);
        var provider = new SessionStructuredFinancialMetricsProvider(service);

        var document = await provider.GetMetricsAsync(
            CreateReport(Guid.NewGuid()),
            CancellationToken.None
        );

        document.Should().BeNull();
    }

    [Fact]
    public async Task Composite_provider_Should_use_session_metrics_first()
    {
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var session = AnalysisSession.Create();
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var service = StructuredFinancialMetricsSessionServiceTests.CreateService(dbContext);
        await service.SaveAsync(
            session.Id,
            StructuredFinancialMetricsSessionServiceTests.CreateInput(
                documentId: "session-document"
            ),
            CancellationToken.None
        );
        var provider = CreateCompositeProvider(service, useFixtureFallback: true);

        var document = await provider.GetMetricsAsync(
            CreateReport(session.Id),
            CancellationToken.None
        );

        document.Should().NotBeNull();
        document!.DocumentId.Should().Be("session-document");
    }

    [Fact]
    public async Task Composite_provider_Should_fall_back_to_fixture_when_session_metrics_are_missing()
    {
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var service = StructuredFinancialMetricsSessionServiceTests.CreateService(dbContext);
        var provider = CreateCompositeProvider(service, useFixtureFallback: true);

        var document = await provider.GetMetricsAsync(
            CreateReport(Guid.NewGuid()),
            CancellationToken.None
        );

        document.Should().NotBeNull();
        document!.Metrics.Should().NotBeEmpty();
        document.DocumentId.Should().Be("vista_energy_sample_metrics");
    }

    private static CompositeStructuredFinancialMetricsProvider CreateCompositeProvider(
        IStructuredFinancialMetricsSessionService service,
        bool useFixtureFallback)
    {
        var options = Options.Create(new DataAgentOptions
        {
            UseFixtureMetricsFallback = useFixtureFallback
        });

        return new CompositeStructuredFinancialMetricsProvider(
            new SessionStructuredFinancialMetricsProvider(service),
            new FixtureStructuredFinancialMetricsProvider(
                options,
                NullLogger<FixtureStructuredFinancialMetricsProvider>.Instance
            ),
            options
        );
    }

    private static FinancialReportContext CreateReport(
        Guid sessionId)
    {
        return new FinancialReportContext(
            SessionId: sessionId,
            ReportName: "vista-energy-report",
            TotalAmount: 125000m,
            TransactionCount: 42,
            SubmittedAt: DateTimeOffset.UtcNow
        );
    }
}
