namespace Orchestration.Application.Activity;

public interface IActivityEventPublisher
{
    Task PublishAsync(
        ActivityEvent activityEvent,
        CancellationToken cancellationToken = default
    );
}