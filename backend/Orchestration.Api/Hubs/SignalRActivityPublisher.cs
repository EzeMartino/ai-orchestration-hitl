using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Orchestration.Application.Activity;
using Orchestration.Application.Persistence;
using Orchestration.Domain.Activity;

namespace Orchestration.Api.Hubs;

public sealed class SignalRActivityEventPublisher : IActivityEventPublisher
{
    private readonly IHubContext<ActivityHub> _hubContext;
    private readonly IOrchestrationDbContext _dbContext;
    private readonly ILogger<SignalRActivityEventPublisher> _logger;

    public SignalRActivityEventPublisher(
        IHubContext<ActivityHub> hubContext,
        IOrchestrationDbContext dbContext,
        ILogger<SignalRActivityEventPublisher> logger)
    {
        _hubContext = hubContext;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task PublishAsync(
        ActivityEvent activityEvent,
        CancellationToken cancellationToken = default)
    {
        var log = ActivityEventLog.Create(
            activityEvent.SessionId,
            activityEvent.Type,
            activityEvent.Agent,
            activityEvent.Message,
            activityEvent.Timestamp
        );

        _dbContext.ActivityEvents.Add(log);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var ownerId = await _dbContext.AnalysisSessions
            .Where(session => session.Id == activityEvent.SessionId)
            .Select(session => (Guid?)session.UserId)
            .SingleOrDefaultAsync(cancellationToken);

        if (ownerId is null)
        {
            _logger.LogWarning(
                "Realtime activity delivery skipped because session ownership was not found.");
            return;
        }

        await _hubContext.Clients
            .User(ownerId.Value.ToString())
            .SendAsync(
                "activityEventReceived",
                activityEvent,
                cancellationToken
            );
    }
}
