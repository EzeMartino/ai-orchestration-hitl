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
        document.Company.Should().Be("Vista Energy");
        document.Currency.Should().Be("USD");
        document.Unit.Should().Be("USD_thousand");
        document.InputSource.Should().Be(FinancialMetricsInputSources.SessionContext);
        document.Provenance.Should().NotBeNull();
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
    public async Task Session_provider_Should_handle_malformed_context_safely()
    {
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var session = AnalysisSession.Create();
        session.SetContext("{\"structuredFinancialMetrics\":\"not-an-object\"}");
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var service = StructuredFinancialMetricsSessionServiceTests.CreateService(dbContext);
        var provider = new SessionStructuredFinancialMetricsProvider(service);

        var document = await provider.GetMetricsAsync(
            CreateReport(session.Id),
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
        document.Company.Should().Be("Vista Energy");
        document.Currency.Should().Be("USD");
        document.Unit.Should().Be("USD_thousand");
        document.InputSource.Should().Be(FinancialMetricsInputSources.SessionContext);
    }

    [Fact]
    public async Task Composite_provider_Should_not_call_fixture_when_session_metrics_exist()
    {
        var sessionDocument = CreateDocument(
            documentId: "session-document",
            company: "Manual Test Co",
            currency: "ARS",
            unit: "ARS_thousand",
            inputSource: FinancialMetricsInputSources.SessionContext
        );
        var sessionProvider = new FakeStructuredFinancialMetricsProvider(sessionDocument);
        var fixtureProvider = new FakeStructuredFinancialMetricsProvider(CreateDocument("fixture-document"));
        var provider = CreateCompositeProvider(
            sessionProvider,
            fixtureProvider,
            useFixtureFallback: true
        );

        var document = await provider.GetMetricsAsync(
            CreateReport(Guid.NewGuid()),
            CancellationToken.None
        );

        document.Should().BeSameAs(sessionDocument);
        document!.InputSource.Should().Be(FinancialMetricsInputSources.SessionContext);
        sessionProvider.CallCount.Should().Be(1);
        fixtureProvider.CallCount.Should().Be(0);
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
        document.InputSource.Should().Be(FinancialMetricsInputSources.FixtureFallback);
        document.Provenance.Should().BeNull();
    }

    [Fact]
    public async Task Composite_provider_Should_preserve_fixture_input_source()
    {
        var provider = CreateCompositeProvider(
            new FakeStructuredFinancialMetricsProvider(null),
            new FakeStructuredFinancialMetricsProvider(CreateDocument(
                "fixture-document",
                inputSource: FinancialMetricsInputSources.FixtureFallback
            )),
            useFixtureFallback: true
        );

        var document = await provider.GetMetricsAsync(
            CreateReport(Guid.NewGuid()),
            CancellationToken.None
        );

        document.Should().NotBeNull();
        document!.InputSource.Should().Be(FinancialMetricsInputSources.FixtureFallback);
    }

    [Fact]
    public async Task Session_provider_Should_propagate_structured_metrics_provenance()
    {
        await using var dbContext = StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
        var session = AnalysisSession.Create();
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var service = StructuredFinancialMetricsSessionServiceTests.CreateService(dbContext);
        await service.SaveAsync(
            new SaveStructuredFinancialMetricsRequest(
                SessionId: session.Id,
                Input: StructuredFinancialMetricsSessionServiceTests.CreateInput(),
                Provenance: new StructuredFinancialMetricsProvenanceInput(
                    IngestionMethod: "json_file",
                    OriginalFileName: "metrics.json",
                    FileSizeBytes: 1024,
                    ContentHash: "abc123"
                )
            ),
            CancellationToken.None
        );
        var provider = new SessionStructuredFinancialMetricsProvider(service);

        var document = await provider.GetMetricsAsync(
            CreateReport(session.Id),
            CancellationToken.None
        );

        document.Should().NotBeNull();
        document!.InputSource.Should().Be(FinancialMetricsInputSources.SessionContext);
        document.Provenance.Should().NotBeNull();
        document.Provenance!.IngestionMethod.Should().Be("json_file");
        document.Provenance.OriginalFileName.Should().Be("metrics.json");
    }

    [Fact]
    public async Task Composite_provider_Should_return_null_when_metrics_are_missing_and_fixture_fallback_is_disabled()
    {
        var provider = CreateCompositeProvider(
            new FakeStructuredFinancialMetricsProvider(null),
            new FakeStructuredFinancialMetricsProvider(CreateDocument("fixture-document")),
            useFixtureFallback: false
        );

        var document = await provider.GetMetricsAsync(
            CreateReport(Guid.NewGuid()),
            CancellationToken.None
        );

        document.Should().BeNull();
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
            options,
            NullLogger<CompositeStructuredFinancialMetricsProvider>.Instance
        );
    }

    private static CompositeStructuredFinancialMetricsProvider CreateCompositeProvider(
        IStructuredFinancialMetricsProvider sessionProvider,
        IStructuredFinancialMetricsProvider fixtureProvider,
        bool useFixtureFallback)
    {
        return new CompositeStructuredFinancialMetricsProvider(
            sessionProvider,
            fixtureProvider,
            Options.Create(new DataAgentOptions
            {
                UseFixtureMetricsFallback = useFixtureFallback
            }),
            NullLogger<CompositeStructuredFinancialMetricsProvider>.Instance
        );
    }

    private static StructuredFinancialMetricsDocument CreateDocument(
        string documentId,
        string company = "Vista Energy",
        string currency = "USD",
        string unit = "USD_thousand",
        string inputSource = FinancialMetricsInputSources.Unknown,
        StructuredFinancialMetricsProvenance? provenance = null)
    {
        return new StructuredFinancialMetricsDocument(
            DocumentId: documentId,
            Company: company,
            Currency: currency,
            Unit: unit,
            Metrics:
            [
                new FinancialMetric(
                    Name: "revenue",
                    Period: "2024A",
                    Value: 100m,
                    Unit: unit,
                    Statement: "unit_test",
                    Source: "unit_test",
                    Currency: currency,
                    SourcePage: 18,
                    Confidence: 0.9m
                )
            ],
            InputSource: inputSource,
            Provenance: provenance
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

    private sealed class FakeStructuredFinancialMetricsProvider
        : IStructuredFinancialMetricsProvider
    {
        private readonly StructuredFinancialMetricsDocument? _document;

        public FakeStructuredFinancialMetricsProvider(
            StructuredFinancialMetricsDocument? document)
        {
            _document = document;
        }

        public int CallCount { get; private set; }

        public Task<StructuredFinancialMetricsDocument?> GetMetricsAsync(
            FinancialReportContext report,
            CancellationToken cancellationToken)
        {
            CallCount++;

            return Task.FromResult(_document);
        }
    }
}
