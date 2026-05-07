using Orchestration.Application.Activity;

namespace Orchestration.Tests.Agents;

public sealed class FakeActivityEventPublisher : IActivityEventPublisher
{
    public List<ActivityEvent> PublishedEvents { get; } = [];

    public Task PublishAsync(
        ActivityEvent activityEvent,
        CancellationToken cancellationToken = default)
    {
        PublishedEvents.Add(activityEvent);

        return Task.CompletedTask;
    }
}