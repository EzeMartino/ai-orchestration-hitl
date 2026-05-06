namespace Orchestration.Domain.Activity;

public class ActivityEventLog
{
    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public string Agent { get; private set; } = string.Empty;

    public string Message { get; private set; } = string.Empty;

    public DateTimeOffset Timestamp { get; private set; }

    private ActivityEventLog()
    {
    }

    public static ActivityEventLog Create(
        Guid sessionId,
        string type,
        string agent,
        string message,
        DateTimeOffset timestamp)
    {
        return new ActivityEventLog
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Type = type,
            Agent = agent,
            Message = message,
            Timestamp = timestamp
        };
    }
}