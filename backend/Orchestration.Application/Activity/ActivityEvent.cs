namespace Orchestration.Application.Activity;

public sealed record ActivityEvent(
    Guid SessionId,
    string Type,
    string Agent,
    string Message,
    DateTimeOffset Timestamp
);