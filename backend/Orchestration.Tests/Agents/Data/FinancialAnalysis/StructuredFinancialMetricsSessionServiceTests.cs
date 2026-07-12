using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Infrastructure.Persistence;
using Orchestration.Tests.Agents;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsSessionServiceTests
{
    [Fact]
    public async Task SaveAsync_Should_return_null_when_session_does_not_exist()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var result = await service.SaveAsync(
            Guid.NewGuid(),
            CreateInput(),
            CancellationToken.None
        );

        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_Should_validate_and_persist_normalized_metrics_when_valid()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create();
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var result = await service.SaveAsync(
            session.Id,
            CreateInput(),
            CancellationToken.None
        );

        result.Should().NotBeNull();
        result!.IsValid.Should().BeTrue();
        result.Context.Should().NotBeNull();
        result.Context!.Metrics.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Name = "revenue",
                Period = "2024A",
                Value = 1647768m,
                Unit = "USD_thousand",
                Currency = "USD",
                Source = "manual_upload",
                SourcePage = 18,
                Confidence = 0.9m
            });

        using var document = JsonDocument.Parse(session.ContextJson);
        var context = document.RootElement.GetProperty("structuredFinancialMetrics");
        context.GetProperty("documentId").GetString().Should().Be("vista-energy-structured-input");
        context.GetProperty("metrics")[0].GetProperty("name").GetString().Should().Be("revenue");
        context.GetProperty("metrics")[0].GetProperty("sourcePage").GetInt32().Should().Be(18);
        context.GetProperty("provenance").GetProperty("ingestionMethod").GetString()
            .Should()
            .Be("unknown");
        context.GetProperty("provenance").GetProperty("metricCount").GetInt32()
            .Should()
            .Be(1);
        context.GetProperty("uploadedAt").GetDateTimeOffset().Should().BeCloseTo(
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(10)
        );
    }

    [Fact]
    public async Task StageAsync_Should_mutate_tracked_session_without_saving_or_publishing()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create();
        session.SetContext("{\"planner\":{\"summary\":\"keep\"}}");
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        dbContext.ResetSaveChangesCount();
        var publisher = new FakeActivityEventPublisher();
        var service = CreateService(dbContext, publisher);

        var result = await service.StageAsync(
            new SaveStructuredFinancialMetricsRequest(
                SessionId: session.Id,
                Input: CreateInput(),
                Provenance: new StructuredFinancialMetricsProvenanceInput(
                    IngestionMethod: "pdf_file_reviewed",
                    OriginalFileName: "report.pdf",
                    FileSizeBytes: 2048,
                    ContentHash: "abc123")),
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.IsValid.Should().BeTrue();
        dbContext.SaveChangesCount.Should().Be(0);
        publisher.PublishedEvents.Should().BeEmpty();
        using var document = JsonDocument.Parse(session.ContextJson);
        document.RootElement.GetProperty("planner").GetProperty("summary")
            .GetString().Should().Be("keep");
        document.RootElement.GetProperty("structuredFinancialMetrics")
            .GetProperty("provenance")
            .GetProperty("ingestionMethod")
            .GetString().Should().Be("pdf_file_reviewed");
    }

    [Fact]
    public async Task SaveAsync_Should_store_json_paste_provenance()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create();
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var publisher = new FakeActivityEventPublisher();
        var service = CreateService(dbContext, publisher);

        var result = await service.SaveAsync(
            new SaveStructuredFinancialMetricsRequest(
                SessionId: session.Id,
                Input: CreateInput(),
                Provenance: new StructuredFinancialMetricsProvenanceInput(
                    IngestionMethod: "json_paste",
                    OriginalFileName: null,
                    FileSizeBytes: null,
                    ContentHash: null
                )
            ),
            CancellationToken.None
        );

        result.Should().NotBeNull();
        result!.Context.Should().NotBeNull();
        result.Context!.Provenance.Should().NotBeNull();
        result.Context.Provenance!.IngestionMethod.Should().Be("json_paste");
        result.Context.Provenance.MetricCount.Should().Be(result.Context.Metrics.Count);
        result.Context.Provenance.WarningCount.Should().Be(result.Warnings.Count);
        publisher.PublishedEvents.Should().ContainSingle(e =>
            e.Type == "structured_financial_metrics_attached" &&
            e.Message.Contains("1 métricas desde json_paste", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task SaveAsync_Should_store_csv_paste_provenance()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create();
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var result = await service.SaveAsync(
            new SaveStructuredFinancialMetricsRequest(
                SessionId: session.Id,
                Input: CreateInput(),
                Provenance: new StructuredFinancialMetricsProvenanceInput(
                    IngestionMethod: "csv_paste",
                    OriginalFileName: null,
                    FileSizeBytes: null,
                    ContentHash: null
                )
            ),
            CancellationToken.None
        );

        result.Should().NotBeNull();
        result!.Context!.Provenance!.IngestionMethod.Should().Be("csv_paste");
    }

    [Fact]
    public async Task SaveAsync_Should_preserve_pdf_file_provenance()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create();
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var publisher = new FakeActivityEventPublisher();
        var service = CreateService(dbContext, publisher);

        var result = await service.SaveAsync(
            new SaveStructuredFinancialMetricsRequest(
                SessionId: session.Id,
                Input: CreateInput(),
                Provenance: new StructuredFinancialMetricsProvenanceInput(
                    IngestionMethod: "pdf_file",
                    OriginalFileName: "report.pdf",
                    FileSizeBytes: 2048,
                    ContentHash: "abc123"
                )
            ),
            CancellationToken.None
        );

        result.Should().NotBeNull();
        result!.IsValid.Should().BeTrue();
        result.Context.Should().NotBeNull();
        result.Context!.Provenance.Should().NotBeNull();
        result.Context.Provenance!.IngestionMethod.Should().Be("pdf_file");
        result.Context.Provenance.OriginalFileName.Should().Be("report.pdf");
        result.Context.Provenance.FileSizeBytes.Should().Be(2048);
        result.Context.Provenance.ContentHash.Should().Be("abc123");
    }

    [Theory]
    [InlineData("pdf_file_reviewed")]
    [InlineData("pdf_file_semantic")]
    public async Task SaveAsync_Should_preserve_supported_pdf_ingestion_methods(
        string ingestionMethod)
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create();
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var result = await service.SaveAsync(
            new SaveStructuredFinancialMetricsRequest(
                SessionId: session.Id,
                Input: CreateInput(),
                Provenance: new StructuredFinancialMetricsProvenanceInput(
                    IngestionMethod: ingestionMethod,
                    OriginalFileName: "report.pdf",
                    FileSizeBytes: 2048,
                    ContentHash: "abc123"
                )
            ),
            CancellationToken.None
        );

        result.Should().NotBeNull();
        result!.Context!.Provenance!.IngestionMethod.Should().Be(ingestionMethod);
    }

    [Fact]
    public async Task SaveAsync_Should_not_emit_event_when_input_is_invalid()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create();
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var publisher = new FakeActivityEventPublisher();
        var service = CreateService(dbContext, publisher);

        var result = await service.SaveAsync(
            new SaveStructuredFinancialMetricsRequest(
                SessionId: session.Id,
                Input: CreateInput(documentId: ""),
                Provenance: new StructuredFinancialMetricsProvenanceInput(
                    IngestionMethod: "json_paste",
                    OriginalFileName: null,
                    FileSizeBytes: null,
                    ContentHash: null
                )
            ),
            CancellationToken.None
        );

        result.Should().NotBeNull();
        result!.IsValid.Should().BeFalse();
        publisher.PublishedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_Should_not_persist_when_financial_input_is_invalid()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create();
        session.SetContext("{\"planner\":{\"summary\":\"keep\"}}");
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var result = await service.SaveAsync(
            session.Id,
            CreateInput(documentId: ""),
            CancellationToken.None
        );

        result.Should().NotBeNull();
        result!.IsValid.Should().BeFalse();
        result.Context.Should().BeNull();
        session.ContextJson.Should().Be("{\"planner\":{\"summary\":\"keep\"}}");
    }

    [Fact]
    public async Task SaveAsync_Should_preserve_existing_context_blocks()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create();
        session.SetContext(
            "{\"planner\":{\"summary\":\"keep planner\"},\"anomaly\":{\"summary\":\"keep anomaly\"}}"
        );
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        await service.SaveAsync(
            session.Id,
            CreateInput(),
            CancellationToken.None
        );

        using var document = JsonDocument.Parse(session.ContextJson);
        document.RootElement.GetProperty("planner").GetProperty("summary")
            .GetString().Should().Be("keep planner");
        document.RootElement.GetProperty("anomaly").GetProperty("summary")
            .GetString().Should().Be("keep anomaly");
        document.RootElement.TryGetProperty("structuredFinancialMetrics", out _)
            .Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_Should_not_change_session_status_or_current_agent()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create();
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        await service.SaveAsync(
            session.Id,
            CreateInput(),
            CancellationToken.None
        );

        session.Status.Should().Be(AnalysisSessionStatus.Pending);
        session.CurrentAgent.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_Should_return_persisted_metrics()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create();
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);
        await service.SaveAsync(session.Id, CreateInput(), CancellationToken.None);

        var context = await service.GetAsync(session.Id, CancellationToken.None);

        context.Should().NotBeNull();
        context!.DocumentId.Should().Be("vista-energy-structured-input");
        context.Metrics.Should().ContainSingle(metric => metric.Name == "revenue");
    }

    [Fact]
    public async Task GetAsync_Should_deserialize_old_context_without_provenance()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create();
        session.SetContext("""
        {
          "structuredFinancialMetrics": {
            "documentId": "legacy-context",
            "company": "Legacy Co",
            "currency": "USD",
            "unit": "USD_thousand",
            "metrics": [
              {
                "name": "revenue",
                "period": "2024A",
                "value": 100,
                "unit": "USD_thousand",
                "statement": "unknown",
                "source": "legacy",
                "currency": "USD",
                "sourcePage": 1,
                "confidence": 0.8
              }
            ],
            "validationWarnings": [],
            "uploadedAt": "2026-05-20T00:00:00Z"
          }
        }
        """);
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var context = await service.GetAsync(session.Id, CancellationToken.None);

        context.Should().NotBeNull();
        context!.DocumentId.Should().Be("legacy-context");
        context.Provenance.Should().BeNull();
    }

    internal static CountingOrchestrationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new CountingOrchestrationDbContext(options);
    }

    internal static StructuredFinancialMetricsSessionService CreateService(
        OrchestrationDbContext dbContext,
        FakeActivityEventPublisher? publisher = null)
    {
        return new StructuredFinancialMetricsSessionService(
            dbContext,
            new StructuredFinancialMetricsValidator(),
            new FinancialMetricInputMapper(),
            publisher ?? new FakeActivityEventPublisher()
        );
    }

    internal static StructuredFinancialMetricsInput CreateInput(
        string documentId = "vista-energy-structured-input",
        IReadOnlyList<StructuredFinancialMetricInput>? metrics = null)
    {
        return new StructuredFinancialMetricsInput(
            DocumentId: documentId,
            Company: "Vista Energy",
            Currency: "USD",
            Unit: "USD_thousand",
            Metrics: metrics ??
            [
                new StructuredFinancialMetricInput(
                    Name: "Revenue",
                    Period: " 2024a ",
                    Value: 1647768m,
                    Unit: null,
                    Currency: null,
                    Source: "manual_upload",
                    SourcePage: 18,
                    Confidence: 0.9m
                )
            ],
            ReportSummary: TestReportSummary.Input
        );
    }

    internal sealed class CountingOrchestrationDbContext(
        DbContextOptions<OrchestrationDbContext> options)
        : OrchestrationDbContext(options)
    {
        public int SaveChangesCount { get; private set; }

        public override Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            return base.SaveChangesAsync(cancellationToken);
        }

        public void ResetSaveChangesCount()
        {
            SaveChangesCount = 0;
        }
    }
}
