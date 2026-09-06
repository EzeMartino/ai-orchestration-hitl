using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orchestration.Api.Hubs;
using Orchestration.Application.Activity;
using Orchestration.Application.Persistence;
using Orchestration.Domain.AnalysisSessions;
using Orchestration.Domain.FinancialMetricsExtraction;
using Orchestration.Infrastructure.Persistence;

namespace Orchestration.Tests.Api.Hubs;

public sealed class SignalRActivityEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_KnownSession_PersistsEventAndSendsOnlyToOwner()
    {
        var options = CreateOptions();
        await using var dbContext = new OrchestrationDbContext(options);
        var ownerId = Guid.NewGuid();
        var unrelatedUserId = Guid.NewGuid();
        var session = AnalysisSession.Create(ownerId);
        var unrelatedSession = AnalysisSession.Create(unrelatedUserId);
        dbContext.AnalysisSessions.AddRange(unrelatedSession, session);
        await dbContext.SaveChangesAsync();

        var hubContext = new RecordingHubContext();
        var publisher = new SignalRActivityEventPublisher(
            hubContext,
            dbContext,
            new RecordingLogger<SignalRActivityEventPublisher>());
        var activityEvent = CreateEvent(session.Id);

        await publisher.PublishAsync(activityEvent);

        var persistedEvent = await dbContext.ActivityEvents.SingleAsync();
        Assert.Equal(activityEvent.SessionId, persistedEvent.SessionId);
        Assert.Equal(activityEvent.Type, persistedEvent.Type);
        Assert.Equal(activityEvent.Agent, persistedEvent.Agent);
        Assert.Equal(activityEvent.Message, persistedEvent.Message);
        Assert.Equal(activityEvent.Timestamp, persistedEvent.Timestamp);

        var ownerMessage = Assert.Single(hubContext.Clients.MessagesForUser(ownerId.ToString()));
        Assert.Equal("activityEventReceived", ownerMessage.Method);
        Assert.Same(activityEvent, Assert.Single(ownerMessage.Arguments));
        Assert.Empty(hubContext.Clients.MessagesForUser(unrelatedUserId.ToString()));
        Assert.Equal([ownerId.ToString()], hubContext.Clients.RequestedUsers);
        Assert.DoesNotContain(unrelatedUserId.ToString(), hubContext.Clients.RequestedUsers);
        Assert.Equal(0, hubContext.Clients.AllAccessCount);
        Assert.Equal(0, hubContext.Clients.GroupAccessCount);
        Assert.Equal(0, hubContext.GroupsAccessCount);
    }

    [Fact]
    public async Task PublishAsync_UnknownSession_PersistsEventAndSkipsDeliveryWithSafeWarning()
    {
        var options = CreateOptions();
        await using var dbContext = new OrchestrationDbContext(options);
        var hubContext = new RecordingHubContext();
        var logger = new RecordingLogger<SignalRActivityEventPublisher>();
        var publisher = new SignalRActivityEventPublisher(
            hubContext,
            dbContext,
            logger);
        var activityEvent = CreateEvent(Guid.NewGuid());

        await publisher.PublishAsync(activityEvent);

        Assert.Equal(1, await dbContext.ActivityEvents.CountAsync());
        Assert.Empty(hubContext.Clients.RequestedUsers);
        Assert.Equal(0, hubContext.Clients.AllAccessCount);
        Assert.Equal(0, hubContext.Clients.GroupAccessCount);
        var warning = Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning);
        Assert.Equal(
            "Realtime activity delivery skipped because session ownership was not found.",
            warning.Message);
        Assert.DoesNotContain(activityEvent.SessionId.ToString(), warning.Message);
        Assert.DoesNotContain(activityEvent.Message, warning.Message);
    }

    [Fact]
    public async Task PublishAsync_OwnershipLookupThrows_PreservesCommittedEventWithoutInterruptingWorkflow()
    {
        var options = CreateOptions();
        await using var realDbContext = new OrchestrationDbContext(options);
        var expectedException = new InvalidOperationException("ownership lookup failed");
        var throwingDbContext = new OwnershipLookupThrowingDbContext(
            realDbContext,
            expectedException);
        var hubContext = new RecordingHubContext();
        var publisher = new SignalRActivityEventPublisher(
            hubContext,
            throwingDbContext,
            new RecordingLogger<SignalRActivityEventPublisher>());
        var activityEvent = CreateEvent(Guid.NewGuid());

        await publisher.PublishAsync(activityEvent);
        await using var verificationContext = new OrchestrationDbContext(options);
        Assert.Equal(1, await verificationContext.ActivityEvents.CountAsync());
        Assert.Empty(hubContext.Clients.RequestedUsers);
        Assert.Equal(0, hubContext.Clients.AllAccessCount);
        Assert.Equal(0, hubContext.Clients.GroupAccessCount);
    }

    [Fact]
    public async Task PublishAsync_CancelledOperation_HonorsCancellationWithoutDelivery()
    {
        var options = CreateOptions();
        await using var dbContext = new OrchestrationDbContext(options);
        var hubContext = new RecordingHubContext();
        var publisher = new SignalRActivityEventPublisher(
            hubContext,
            dbContext,
            new RecordingLogger<SignalRActivityEventPublisher>());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => publisher.PublishAsync(
                CreateEvent(Guid.NewGuid()),
                cancellation.Token));

        await using var verificationContext = new OrchestrationDbContext(options);
        Assert.Equal(0, await verificationContext.ActivityEvents.CountAsync());
        Assert.Empty(hubContext.Clients.RequestedUsers);
        Assert.Equal(0, hubContext.Clients.AllAccessCount);
        Assert.Equal(0, hubContext.Clients.GroupAccessCount);
    }

    private static DbContextOptions<OrchestrationDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    private static ActivityEvent CreateEvent(Guid sessionId)
    {
        return new ActivityEvent(
            sessionId,
            "analysis.updated",
            "Planner",
            "sensitive activity detail",
            DateTimeOffset.UtcNow);
    }

    private sealed class OwnershipLookupThrowingDbContext(
        OrchestrationDbContext inner,
        Exception exception) : IOrchestrationDbContext
    {
        public DbSet<AnalysisSession> AnalysisSessions => throw exception;

        public DbSet<Orchestration.Domain.Activity.ActivityEventLog> ActivityEvents =>
            inner.ActivityEvents;

        public DbSet<FinancialMetricsExtractionDraft> FinancialMetricsExtractionDrafts =>
            inner.FinancialMetricsExtractionDrafts;

        public void ClearTrackedChanges()
        {
            inner.ClearTrackedChanges();
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return inner.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class RecordingHubContext : IHubContext<ActivityHub>
    {
        private readonly RecordingGroupManager _groups = new();

        public RecordingHubClients Clients { get; } = new();

        IHubClients IHubContext<ActivityHub>.Clients => Clients;

        public int GroupsAccessCount { get; private set; }

        public IGroupManager Groups
        {
            get
            {
                GroupsAccessCount++;
                return _groups;
            }
        }
    }

    private sealed class RecordingHubClients : IHubClients
    {
        private readonly Dictionary<string, RecordingClientProxy> _userClients = [];
        private readonly RecordingClientProxy _unusedProxy = new();

        public List<string> RequestedUsers { get; } = [];

        public int AllAccessCount { get; private set; }

        public int GroupAccessCount { get; private set; }

        public IClientProxy All
        {
            get
            {
                AllAccessCount++;
                return _unusedProxy;
            }
        }

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds)
        {
            AllAccessCount++;
            return _unusedProxy;
        }

        public IClientProxy Client(string connectionId)
        {
            return _unusedProxy;
        }

        public IClientProxy Clients(IReadOnlyList<string> connectionIds)
        {
            return _unusedProxy;
        }

        public IClientProxy Group(string groupName)
        {
            GroupAccessCount++;
            return _unusedProxy;
        }

        public IClientProxy GroupExcept(
            string groupName,
            IReadOnlyList<string> excludedConnectionIds)
        {
            GroupAccessCount++;
            return _unusedProxy;
        }

        public IClientProxy Groups(IReadOnlyList<string> groupNames)
        {
            GroupAccessCount++;
            return _unusedProxy;
        }

        public IClientProxy User(string userId)
        {
            RequestedUsers.Add(userId);

            if (!_userClients.TryGetValue(userId, out var client))
            {
                client = new RecordingClientProxy();
                _userClients.Add(userId, client);
            }

            return client;
        }

        public IClientProxy Users(IReadOnlyList<string> userIds)
        {
            return _unusedProxy;
        }

        public IReadOnlyList<SentMessage> MessagesForUser(string userId)
        {
            return _userClients.TryGetValue(userId, out var client)
                ? client.Messages
                : [];
        }
    }

    private sealed class RecordingClientProxy : IClientProxy
    {
        public List<SentMessage> Messages { get; } = [];

        public Task SendCoreAsync(
            string method,
            object?[] args,
            CancellationToken cancellationToken = default)
        {
            Messages.Add(new SentMessage(method, args, cancellationToken));
            return Task.CompletedTask;
        }
    }

    private sealed record SentMessage(
        string Method,
        IReadOnlyList<object?> Arguments,
        CancellationToken CancellationToken);

    private sealed class RecordingGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
