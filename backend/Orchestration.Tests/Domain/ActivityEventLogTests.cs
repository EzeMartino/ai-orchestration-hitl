using Orchestration.Domain.Activity;

namespace Orchestration.Tests.Domain;

public class ActivityEventLogTests
{
    [Fact]
    public void Create_Should_truncate_messages_that_exceed_persistence_limit()
    {
        var longMessage = new string('A', 2_500);

        var log = ActivityEventLog.Create(
            Guid.NewGuid(),
            "data_agent_ai_review_completed",
            "DataAgent",
            longMessage,
            DateTimeOffset.UtcNow);

        Assert.True(log.Message.Length <= ActivityEventLog.MessageMaxLength);
        Assert.EndsWith(" [truncated]", log.Message);
    }
}
