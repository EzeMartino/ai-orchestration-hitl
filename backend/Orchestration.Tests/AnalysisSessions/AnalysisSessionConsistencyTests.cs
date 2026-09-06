using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Orchestration.Api.Hubs;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Planner;
using Orchestration.Application.Agents.Planner.Reasoning;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Application.Agents.Shared;
using Orchestration.Application.AnalysisSessions;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Infrastructure.Persistence;

namespace Orchestration.Tests.AnalysisSessions;

public sealed class AnalysisSessionConsistencyTests
{
    [Fact]
    public async Task PlannerFailure_PersistsSafeFailedStateAndAudit()
    {
        await using var db = MemoryDatabase();
        var session = await SeedAsync(db);
        var planner = new TestPlanner(_ => throw new InvalidOperationException("secret-provider-detail"));

        var result = await Service(db, planner).StartAnalysisAsync(session.Id, CancellationToken.None);

        Assert.Equal("Failed", result!.Status);
        var saved = await db.AnalysisSessions.AsNoTracking().SingleAsync();
        Assert.Equal(AnalysisSessionStatus.Failed, saved.Status);
        Assert.Null(saved.CurrentAgent);
        Assert.NotNull(saved.CompletedAt);
        Assert.DoesNotContain("secret-provider-detail", saved.FailureReason!);
        var audit = await db.ActivityEvents.AsNoTracking().ToListAsync();
        Assert.Contains(audit, e => e.Type == "analysis_failed");
        Assert.DoesNotContain(audit, e => e.Message.Contains("secret-provider-detail"));
    }

    [Fact]
    public async Task PlannerCancellation_PersistsFailedStateAndPropagatesCancellation()
    {
        await using var db = MemoryDatabase();
        var session = await SeedAsync(db);
        using var cancellation = new CancellationTokenSource();
        var planner = new TestPlanner(token =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Result());
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(db, planner).StartAnalysisAsync(session.Id, cancellation.Token));

        var saved = await db.AnalysisSessions.AsNoTracking().SingleAsync();
        Assert.Equal(AnalysisSessionStatus.Failed, saved.Status);
        Assert.Null(saved.CurrentAgent);
        Assert.Contains(await db.ActivityEvents.ToListAsync(), e => e.Type == "analysis_failed");
    }

    [Fact]
    public async Task InitialNotificationCancellation_DoesNotLeaveSessionRunning()
    {
        await using var db = MemoryDatabase();
        var session = await SeedAsync(db);
        using var cancellation = new CancellationTokenSource();
        var hub = new TestHub(() =>
        {
            cancellation.Cancel();
            return Task.FromCanceled(cancellation.Token);
        });
        var planner = new TestPlanner(_ => Task.FromResult(Result()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(db, planner, hub).StartAnalysisAsync(session.Id, cancellation.Token));

        var saved = await db.AnalysisSessions.AsNoTracking().SingleAsync();
        Assert.Equal(AnalysisSessionStatus.Failed, saved.Status);
        Assert.Null(saved.CurrentAgent);
        Assert.Equal(0, planner.Runs);
        Assert.Contains(await db.ActivityEvents.ToListAsync(), e => e.Type == "analysis_failed");
    }

    [Fact]
    public async Task NotificationFailure_DoesNotInterruptPersistedWorkflow()
    {
        await using var db = MemoryDatabase();
        var session = await SeedAsync(db);
        var hub = new TestHub(() => throw new IOException("transport unavailable"));

        var result = await Service(db, new TestPlanner(_ => Task.FromResult(Result())), hub)
            .StartAnalysisAsync(session.Id, CancellationToken.None);

        Assert.Equal("AwaitingHumanApproval", result!.Status);
        Assert.Equal(AnalysisSessionStatus.AwaitingHumanApproval,
            (await db.AnalysisSessions.AsNoTracking().SingleAsync()).Status);
        Assert.Contains(await db.ActivityEvents.ToListAsync(), e => e.Type == "human_approval_required");
    }

    [Fact]
    public async Task DecisionNotification_ObservesCommittedTerminalState()
    {
        await using var db = MemoryDatabase();
        var session = await SeedAsync(db, AnalysisSessionStatus.AwaitingHumanApproval);
        AnalysisSessionStatus? observedStatus = null;
        var hub = new TestHub(async () =>
        {
            observedStatus ??= (await db.AnalysisSessions.AsNoTracking().SingleAsync()).Status;
        });

        await Service(db, new TestPlanner(_ => Task.FromResult(Result())), hub)
            .ApproveAsync(session.Id, new HumanDecisionDto("Reviewed"), CancellationToken.None);

        Assert.Equal(AnalysisSessionStatus.Completed, observedStatus);
    }

    [PostgresFact]
    public async Task CompetingDecisions_OnlyWinnerStateAndAuditPersist()
    {
        await using var database = await TestPostgresDatabase.CreateAsync();
        await using var first = database.CreateContext();
        var session = await SeedAsync(first, AnalysisSessionStatus.AwaitingHumanApproval);
        await using var second = database.CreateContext();
        await second.AnalysisSessions.SingleAsync(s => s.Id == session.Id);

        await Service(first).ApproveAsync(session.Id, new HumanDecisionDto("Accepted"), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(second).RejectAsync(session.Id, new HumanDecisionDto("Rejected"), CancellationToken.None));

        await using var verification = database.CreateContext();
        Assert.Equal(AnalysisSessionStatus.Completed,
            (await verification.AnalysisSessions.SingleAsync()).Status);
        var decisions = await verification.ActivityEvents.Where(e => e.Type == "human_decision_received").ToListAsync();
        Assert.Single(decisions);
        Assert.Contains("Accepted", decisions[0].Message);
        Assert.Empty(second.ChangeTracker.Entries());
    }

    [PostgresFact]
    public async Task CompetingStarts_OnlyWinnerRunsPlannerAndClaimsSession()
    {
        await using var database = await TestPostgresDatabase.CreateAsync();
        await using var first = database.CreateContext();
        var session = await SeedAsync(first);
        await using var second = database.CreateContext();
        await second.AnalysisSessions.SingleAsync(s => s.Id == session.Id);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<PlannerAgentResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var winner = new TestPlanner(_ =>
        {
            entered.SetResult();
            return finish.Task;
        });
        var loser = new TestPlanner(_ => Task.FromResult(Result()));
        var running = Service(first, winner).StartAnalysisAsync(session.Id, CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Service(second, loser).StartAnalysisAsync(session.Id, CancellationToken.None));
            Assert.Equal(0, loser.Runs);
        }
        finally
        {
            finish.TrySetResult(Result());
            await running;
        }

        await using var verification = database.CreateContext();
        Assert.Equal(AnalysisSessionStatus.AwaitingHumanApproval,
            (await verification.AnalysisSessions.SingleAsync()).Status);
        Assert.Equal(1, await verification.ActivityEvents.CountAsync(e => e.Type == "state_transition_requested"));
    }

    [PostgresFact]
    public async Task FailedDecisionSave_RollsBackDecisionEvent()
    {
        await using var database = await TestPostgresDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var session = await SeedAsync(db, AnalysisSessionStatus.AwaitingHumanApproval);

        await Assert.ThrowsAsync<DbUpdateException>(() => Service(db).RejectAsync(
            session.Id, new HumanDecisionDto(new string('x', 1001)), CancellationToken.None));

        await using var verification = database.CreateContext();
        Assert.Equal(AnalysisSessionStatus.AwaitingHumanApproval,
            (await verification.AnalysisSessions.SingleAsync()).Status);
        Assert.Equal(0, await verification.ActivityEvents.CountAsync(e => e.Type == "human_decision_received"));
    }

    internal static async Task<AnalysisSession> SeedAsync(
        OrchestrationDbContext db, AnalysisSessionStatus status = AnalysisSessionStatus.Pending)
    {
        var session = TestFinancialReport.CreateSession();
        session.SetStatus(status);
        db.Users.Add(new IdentityUser<Guid> { Id = session.UserId });
        db.AnalysisSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    private static OrchestrationDbContext MemoryDatabase() => new(
        new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static AnalysisOrchestratorService Service(
        OrchestrationDbContext db, TestPlanner? planner = null, TestHub? hub = null) => new(
        db, new AnalysisSessionWorkflowService(new AnalysisSessionStateMachine()),
        new SignalRActivityEventPublisher(hub ?? new TestHub(), db,
            NullLogger<SignalRActivityEventPublisher>.Instance),
        planner ?? new TestPlanner(_ => Task.FromResult(Result())),
        new FinancialReportContextResolver());

    private static PlannerAgentResult Result() => new(
        true, "Human review required", new DataAgentResult(false, "Low", "Data", "Test", []),
        new LegalAgentResult(false, "NotEstablished", "Review", "Test", [], [], RequiresHumanReview: true),
        new PlannerReasoningResult("Test", "Review", [], [], [], false, false, null, null, null),
        ToolPlanAuditResult.Empty);

    private sealed class TestPlanner(Func<CancellationToken, Task<PlannerAgentResult>> run) : IPlannerAgent
    {
        public int Runs { get; private set; }
        public Task<PlannerAgentResult> RunAsync(FinancialReportContext report, CancellationToken cancellationToken)
        {
            Runs++;
            return run(cancellationToken);
        }
    }

    private sealed class TestHub(Func<Task>? send = null) : IHubContext<ActivityHub>, IHubClients, IClientProxy
    {
        public IHubClients Clients => this;
        public IGroupManager Groups => throw new NotSupportedException();
        public IClientProxy All => this;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => this;
        public IClientProxy Client(string connectionId) => this;
        IClientProxy IHubClients<IClientProxy>.Clients(IReadOnlyList<string> connectionIds) => this;
        public IClientProxy Group(string groupName) => this;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => this;
        IClientProxy IHubClients<IClientProxy>.Groups(IReadOnlyList<string> groupNames) => this;
        public IClientProxy User(string userId) => this;
        public IClientProxy Users(IReadOnlyList<string> userIds) => this;
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
            send?.Invoke() ?? Task.CompletedTask;
    }
}
