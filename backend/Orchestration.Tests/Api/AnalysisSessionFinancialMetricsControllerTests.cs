using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Orchestration.Api.Controllers;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Infrastructure.Persistence;
using Orchestration.Tests.Agents.Data.FinancialAnalysis;

namespace Orchestration.Tests.Api;

public sealed class AnalysisSessionFinancialMetricsControllerTests
{
    [Fact]
    public async Task SaveFinancialMetrics_Should_return_ok_and_persist_context_for_valid_metrics()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create();
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetrics(
            session.Id,
            StructuredFinancialMetricsSessionServiceTests.CreateInput(),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<SaveFinancialMetricsResponse>()
            .Subject;
        response.IsValid.Should().BeTrue();
        response.Context.Should().NotBeNull();
        response.Context!.Metrics.Should().ContainSingle(metric => metric.Name == "revenue");
        session.ContextJson.Should().Contain("structuredFinancialMetrics");
    }

    [Fact]
    public async Task SaveFinancialMetrics_Should_return_ok_with_invalid_result_for_invalid_financial_input()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create();
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetrics(
            session.Id,
            StructuredFinancialMetricsSessionServiceTests.CreateInput(documentId: ""),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<SaveFinancialMetricsResponse>()
            .Subject;
        response.IsValid.Should().BeFalse();
        response.Context.Should().BeNull();
        response.Errors.Should().Contain(issue => issue.Code == "DOCUMENT_ID_REQUIRED");
        session.ContextJson.Should().Be("{}");
    }

    [Fact]
    public async Task SaveFinancialMetrics_Should_return_not_found_for_unknown_session()
    {
        await using var dbContext = CreateDbContext();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetrics(
            Guid.NewGuid(),
            StructuredFinancialMetricsSessionServiceTests.CreateInput(),
            CancellationToken.None
        );

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task SaveFinancialMetrics_Should_return_bad_request_for_null_body()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create();
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetrics(
            session.Id,
            null!,
            CancellationToken.None
        );

        result.Should().BeOfType<BadRequestResult>();
    }

    [Fact]
    public async Task SaveFinancialMetricsCsv_Should_return_ok_and_persist_context_for_valid_csv()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create();
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsCsv(
            session.Id,
            CreateCsvInput(),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<SaveFinancialMetricsResponse>()
            .Subject;
        response.IsValid.Should().BeTrue();
        response.Context.Should().NotBeNull();
        response.Context!.DocumentId.Should().Be("manual-csv-input");
        response.Context.Company.Should().Be("Manual Test Co");
        response.Context.Metrics.Should().Contain(metric =>
            metric.Name == "revenue" &&
            metric.Period == "2024A" &&
            metric.Value == 1647768m
        );
        session.ContextJson.Should().Contain("structuredFinancialMetrics");
    }

    [Fact]
    public async Task SaveFinancialMetricsCsv_Should_return_invalid_result_without_persisting_for_invalid_csv()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create();
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsCsv(
            session.Id,
            CreateCsvInput(csv: """
                name,value
                Revenue,1647768
                """),
            CancellationToken.None
        );

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<SaveFinancialMetricsResponse>()
            .Subject;
        response.IsValid.Should().BeFalse();
        response.Context.Should().BeNull();
        response.Errors.Should().Contain(issue => issue.Code == "CSV_REQUIRED_HEADER_MISSING");
        session.ContextJson.Should().Be("{}");
    }

    [Fact]
    public async Task SaveFinancialMetricsCsv_Should_return_not_found_for_unknown_session()
    {
        await using var dbContext = CreateDbContext();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsCsv(
            Guid.NewGuid(),
            CreateCsvInput(),
            CancellationToken.None
        );

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task SaveFinancialMetricsCsv_Should_return_bad_request_for_null_body()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create();
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.SaveFinancialMetricsCsv(
            session.Id,
            null!,
            CancellationToken.None
        );

        result.Should().BeOfType<BadRequestResult>();
    }

    [Fact]
    public async Task GetFinancialMetrics_Should_return_context_for_session()
    {
        await using var dbContext = CreateDbContext();
        var session = AnalysisSession.Create();
        dbContext.AnalysisSessions.Add(session);
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);
        await controller.SaveFinancialMetrics(
            session.Id,
            StructuredFinancialMetricsSessionServiceTests.CreateInput(),
            CancellationToken.None
        );

        var result = await controller.GetFinancialMetrics(
            session.Id,
            CancellationToken.None
        );

        var response = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<GetFinancialMetricsResponse>()
            .Subject;
        response.SessionId.Should().Be(session.Id);
        response.Context.Should().NotBeNull();
        response.Context!.Metrics.Should().ContainSingle(metric => metric.Name == "revenue");
    }

    [Fact]
    public async Task GetFinancialMetrics_Should_return_not_found_for_unknown_session()
    {
        await using var dbContext = CreateDbContext();
        var controller = CreateController(dbContext);

        var result = await controller.GetFinancialMetrics(
            Guid.NewGuid(),
            CancellationToken.None
        );

        result.Should().BeOfType<NotFoundResult>();
    }

    private static AnalysisSessionsController CreateController(
        OrchestrationDbContext dbContext)
    {
        return new AnalysisSessionsController(
            dbContext,
            orchestrator: null!,
            StructuredFinancialMetricsSessionServiceTests.CreateService(dbContext),
            new StructuredFinancialMetricsCsvParser()
        );
    }

    private static OrchestrationDbContext CreateDbContext()
    {
        return StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
    }

    private static StructuredFinancialMetricsCsvInput CreateCsvInput(
        string csv = """
            name,period,value,unit,currency,source,sourcePage,confidence
            Revenue,2024A,1647768,USD_thousand,USD,manual_upload,18,0.9
            Gross Profit,2024A,924000,USD_thousand,USD,manual_upload,18,0.85
            """)
    {
        return new StructuredFinancialMetricsCsvInput(
            DocumentId: "manual-csv-input",
            Company: "Manual Test Co",
            Currency: "USD",
            Unit: "USD_thousand",
            Csv: csv
        );
    }
}
