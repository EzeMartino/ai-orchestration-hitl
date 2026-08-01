using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Orchestration.Api.Hubs;

[Authorize]
public class ActivityHub : Hub
{
}
