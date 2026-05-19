using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Infrastructure.Persistence;

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
        context.GetProperty("uploadedAt").GetDateTimeOffset().Should().BeCloseTo(
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(10)
        );
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

    internal static OrchestrationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new OrchestrationDbContext(options);
    }

    internal static StructuredFinancialMetricsSessionService CreateService(
        OrchestrationDbContext dbContext)
    {
        return new StructuredFinancialMetricsSessionService(
            dbContext,
            new StructuredFinancialMetricsValidator(),
            new FinancialMetricInputMapper()
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
            ]
        );
    }
}
