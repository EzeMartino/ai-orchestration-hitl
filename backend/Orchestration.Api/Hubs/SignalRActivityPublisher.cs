using Microsoft.AspNetCore.SignalR;
using Orchestration.Api.Hubs;
using Orchestration.Application.Activity;

namespace Orchestration.Api.Hubs;

public sealed class SignalRActivityEventPublisher : IActivityEventPublisher
{
    private readonly IHubContext<ActivityHub> _hubContext;

    public SignalRActivityEventPublisher(IHubContext<ActivityHub> hubContext)
    {
        _hubContext = hubContext;
    }   

    public async Task PublishAsync(
        ActivityEvent activityEvent,
        CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.All.SendAsync(
            "activityEventReceived",
            activityEvent,
            cancellationToken
        );
    }
}