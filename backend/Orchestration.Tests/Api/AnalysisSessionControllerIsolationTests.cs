using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Orchestration.Api.Controllers;
using Orchestration.Application.Activity;
using Orchestration.Application.AnalysisSessions;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;
using Orchestration.Application.Agents.Shared;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Infrastructure.Persistence;
using Orchestration.Tests.Agents;
using Orchestration.Tests.Agents.Data.FinancialAnalysis;
using Xunit;

namespace Orchestration.Tests.Api;

public class AnalysisSessionControllerIsolationTests
{
    private static readonly Guid UserAId = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid UserBId = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static OrchestrationDbContext CreateDbContext()
    {
        return StructuredFinancialMetricsSessionServiceTests.CreateDbContext();
    }

    private static AnalysisSessionsController CreateController(
        OrchestrationDbContext dbContext,
        Guid currentUserId)
    {
        var publisher = new FakeActivityEventPublisher();

        var controller = new AnalysisSessionsController(
            dbContext,
            orchestrator: null!, // Not executing backend service orchestrator logic, controller checks take precedence
            new AnalysisSessionStartPreflightValidator(
                Options.Create(new DataAgentOptions())
            ),
            publisher,
            StructuredFinancialMetricsSessionServiceTests.CreateService(dbContext, publisher),
            new StructuredFinancialMetricsCsvParser(),
            new FakeStructuredFinancialMetricsPdfIngestionService(),
            new FakeFinancialMetricsExtractionDraftService(),
            Options.Create(new StructuredFinancialMetricsFileUploadOptions())
        );

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, currentUserId.ToString()),
            new(ClaimTypes.Name, $"user_{currentUserId}@ezemartino.com")
        };
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };

        return controller;
    }

    private sealed class FakeStructuredFinancialMetricsPdfIngestionService
        : IStructuredFinancialMetricsPdfIngestionService
    {
        public Task<StructuredFinancialMetricsPdfIngestionResult> IngestAsync(
            StructuredFinancialMetricsPdfIngestionRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new StructuredFinancialMetricsPdfIngestionResult(
                FinancialMetricsFileOutcome.Failed,
                SaveResult: null,
                ReviewDraft: null,
                Errors: [],
                Warnings: []
            ));
        }
    }

    private sealed class FakeFinancialMetricsExtractionDraftService
        : IFinancialMetricsExtractionDraftService
    {
        public Task<FinancialMetricsExtractionDraftServiceResult> CreateOrReplaceAsync(
            Guid sessionId,
            Guid userId,
            CreateFinancialMetricsExtractionDraftRequest request,
            CancellationToken cancellationToken) => NotFound();

        public Task<FinancialMetricsExtractionDraftServiceResult> GetPendingAsync(
            Guid sessionId,
            Guid userId,
            CancellationToken cancellationToken) => NotFound();

        public Task<FinancialMetricsExtractionDraftServiceResult> UpdateAsync(
            Guid draftId,
            Guid sessionId,
            Guid userId,
            UpdateFinancialMetricsExtractionDraftRequest request,
            CancellationToken cancellationToken) => NotFound();

        public Task<FinancialMetricsExtractionDraftServiceResult> ConfirmAsync(
            Guid draftId,
            Guid sessionId,
            Guid userId,
            CancellationToken cancellationToken) => NotFound();

        public Task<FinancialMetricsExtractionDraftServiceResult> DiscardAsync(
            Guid draftId,
            Guid sessionId,
            Guid userId,
            CancellationToken cancellationToken) => NotFound();

        private static Task<FinancialMetricsExtractionDraftServiceResult> NotFound()
        {
            return Task.FromResult(
                FinancialMetricsExtractionDraftServiceResult.NotFound("Draft not found.")
            );
        }
    }

    [Fact]
    public async Task GetSessions_Should_only_return_sessions_belonging_to_current_user()
    {
        await using var dbContext = CreateDbContext();
        var sessionA = AnalysisSession.Create(UserAId);
        var sessionB = AnalysisSession.Create(UserBId);
        dbContext.AnalysisSessions.AddRange(sessionA, sessionB);
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, UserAId);

        var result = await controller.GetSessions(CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var sessions = okResult.Value.Should().BeAssignableTo<System.Collections.IEnumerable>().Subject;
        var list = new List<object>();
        foreach (var item in sessions)
        {
            list.Add(item);
        }

        list.Should().ContainSingle();
        var returnedSession = list[0];
        returnedSession.GetType().GetProperty("Id")!.GetValue(returnedSession).Should().Be(sessionA.Id);
    }

    [Fact]
    public async Task GetSession_Should_return_not_found_when_session_belongs_to_different_user()
    {
        await using var dbContext = CreateDbContext();
        var sessionA = AnalysisSession.Create(UserAId);
        dbContext.AnalysisSessions.Add(sessionA);
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, UserBId);

        var result = await controller.GetSession(sessionA.Id, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task StartSession_Should_return_not_found_when_session_belongs_to_different_user()
    {
        await using var dbContext = CreateDbContext();
        var sessionA = AnalysisSession.Create(UserAId);
        dbContext.AnalysisSessions.Add(sessionA);
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, UserBId);

        var result = await controller.StartSession(sessionA.Id, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetStartPreflight_Should_return_not_found_when_session_belongs_to_different_user()
    {
        await using var dbContext = CreateDbContext();
        var sessionA = AnalysisSession.Create(UserAId);
        dbContext.AnalysisSessions.Add(sessionA);
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, UserBId);

        var result = await controller.GetStartPreflight(sessionA.Id, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task ApproveSession_Should_return_not_found_when_session_belongs_to_different_user()
    {
        await using var dbContext = CreateDbContext();
        var sessionA = AnalysisSession.Create(UserAId);
        dbContext.AnalysisSessions.Add(sessionA);
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, UserBId);

        var result = await controller.ApproveSession(
            sessionA.Id,
            new HumanDecisionDto("Approval from B"),
            CancellationToken.None
        );

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task RejectSession_Should_return_not_found_when_session_belongs_to_different_user()
    {
        await using var dbContext = CreateDbContext();
        var sessionA = AnalysisSession.Create(UserAId);
        dbContext.AnalysisSessions.Add(sessionA);
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, UserBId);

        var result = await controller.RejectSession(
            sessionA.Id,
            new HumanDecisionDto("Rejection from B"),
            CancellationToken.None
        );

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetSessionEvents_Should_return_not_found_when_session_belongs_to_different_user()
    {
        await using var dbContext = CreateDbContext();
        var sessionA = AnalysisSession.Create(UserAId);
        dbContext.AnalysisSessions.Add(sessionA);
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, UserBId);

        var result = await controller.GetSessionEvents(sessionA.Id, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task SaveFinancialMetrics_Should_return_not_found_when_session_belongs_to_different_user()
    {
        await using var dbContext = CreateDbContext();
        var sessionA = AnalysisSession.Create(UserAId);
        dbContext.AnalysisSessions.Add(sessionA);
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, UserBId);

        var input = new StructuredFinancialMetricsInput(
            DocumentId: "doc-id",
            Company: "Company",
            Currency: "USD",
            Unit: "USD_thousand",
            Metrics: []
        );

        var result = await controller.SaveFinancialMetrics(sessionA.Id, input, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task SaveFinancialMetricsCsv_Should_return_not_found_when_session_belongs_to_different_user()
    {
        await using var dbContext = CreateDbContext();
        var sessionA = AnalysisSession.Create(UserAId);
        dbContext.AnalysisSessions.Add(sessionA);
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, UserBId);

        var input = new StructuredFinancialMetricsCsvInput(
            DocumentId: "doc-id",
            Company: "Company",
            Currency: "USD",
            Unit: "USD_thousand",
            Csv: "name,period,value\nRevenue,2024A,100"
        );

        var result = await controller.SaveFinancialMetricsCsv(sessionA.Id, input, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task SaveFinancialMetricsFile_Should_return_not_found_when_session_belongs_to_different_user()
    {
        await using var dbContext = CreateDbContext();
        var sessionA = AnalysisSession.Create(UserAId);
        dbContext.AnalysisSessions.Add(sessionA);
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, UserBId);

        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}"));
        var file = new FormFile(stream, 0, stream.Length, "file", "metrics.json");
        var request = new StructuredFinancialMetricsFileUploadRequest
        {
            File = file,
            DocumentId = "doc-id"
        };

        var result = await controller.SaveFinancialMetricsFile(sessionA.Id, request, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetFinancialMetrics_Should_return_not_found_when_session_belongs_to_different_user()
    {
        await using var dbContext = CreateDbContext();
        var sessionA = AnalysisSession.Create(UserAId);
        dbContext.AnalysisSessions.Add(sessionA);
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, UserBId);

        var result = await controller.GetFinancialMetrics(sessionA.Id, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task FinancialMetrics_TwoUsersAndSessions_ShouldKeepReportSummariesIsolated()
    {
        await using var dbContext = CreateDbContext();
        var sessionA = AnalysisSession.Create(UserAId);
        var sessionB = AnalysisSession.Create(UserBId);
        dbContext.AnalysisSessions.AddRange(sessionA, sessionB);
        await dbContext.SaveChangesAsync();
        var controllerA = CreateController(dbContext, UserAId);
        var controllerB = CreateController(dbContext, UserBId);
        var summaryA = new FinancialReportSummaryInput(
            "  balance-sheet-2025.pdf  ",
            842350.75m,
            187,
            new DateTimeOffset(2026, 7, 12, 18, 30, 0, TimeSpan.Zero));
        var summaryB = new FinancialReportSummaryInput(
            "user-b-report.pdf",
            99001m,
            23,
            new DateTimeOffset(2026, 7, 13, 9, 0, 0, TimeSpan.Zero));

        await controllerA.SaveFinancialMetrics(
            sessionA.Id,
            CreateMetricsInput("user-a-document", summaryA),
            CancellationToken.None);
        await controllerB.SaveFinancialMetrics(
            sessionB.Id,
            CreateMetricsInput("user-b-document", summaryB),
            CancellationToken.None);

        var getA = await controllerA.GetFinancialMetrics(sessionA.Id, CancellationToken.None);
        var getB = await controllerB.GetFinancialMetrics(sessionB.Id, CancellationToken.None);
        var responseA = getA.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<GetFinancialMetricsResponse>().Subject;
        var responseB = getB.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<GetFinancialMetricsResponse>().Subject;
        responseA.ReportSummary.Should().Be(new FinancialReportSummary(
            "balance-sheet-2025.pdf",
            842350.75m,
            187,
            new DateTimeOffset(2026, 7, 12, 18, 30, 0, TimeSpan.Zero)));
        responseB.ReportSummary.Should().Be(new FinancialReportSummary(
            "user-b-report.pdf",
            99001m,
            23,
            new DateTimeOffset(2026, 7, 13, 9, 0, 0, TimeSpan.Zero)));

        var forbiddenRead = await controllerA.GetFinancialMetrics(
            sessionB.Id,
            CancellationToken.None);
        var forbiddenWrite = await controllerA.SaveFinancialMetrics(
            sessionB.Id,
            CreateMetricsInput("cross-session-write", summaryA),
            CancellationToken.None);

        forbiddenRead.Should().BeOfType<NotFoundResult>();
        forbiddenWrite.Should().BeOfType<NotFoundResult>();
        var unchangedB = await controllerB.GetFinancialMetrics(
            sessionB.Id,
            CancellationToken.None);
        unchangedB.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<GetFinancialMetricsResponse>()
            .Which.ReportSummary.Should().Be(responseB.ReportSummary);
    }

    private static StructuredFinancialMetricsInput CreateMetricsInput(
        string documentId,
        FinancialReportSummaryInput reportSummary)
    {
        return StructuredFinancialMetricsSessionServiceTests.CreateInput(documentId) with
        {
            ReportSummary = reportSummary
        };
    }
}
