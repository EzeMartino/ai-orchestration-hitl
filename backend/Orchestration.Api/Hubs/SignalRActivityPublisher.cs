using Microsoft.AspNetCore.SignalR;
using Orchestration.Application.Activity;
using Orchestration.Domain.Activity;
using Orchestration.Application.Persistence;

namespace Orchestration.Api.Hubs;

public sealed class SignalRActivityEventPublisher : IActivityEventPublisher
{
    private readonly IHubContext<ActivityHub> _hubContext;
    private readonly IOrchestrationDbContext _dbContext;

    public SignalRActivityEventPublisher(
        IHubContext<ActivityHub> hubContext,
        IOrchestrationDbContext dbContext)
    {
        _hubContext = hubContext;
        _dbContext = dbContext;
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

        await _hubContext.Clients.All.SendAsync(
            "activityEventReceived",
            activityEvent,
            cancellationToken
        );
    }
}