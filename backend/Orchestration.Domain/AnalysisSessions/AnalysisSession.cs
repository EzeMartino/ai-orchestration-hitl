using System;
using System.Collections.Generic;
using System.Text;

namespace Orchestration.Domain.AnalysisSessions;
public class AnalysisSession
{
    public Guid Id { get; private set; }

    public AnalysisSessionStatus Status { get; private set; }

    public string ContextJson { get; private set; } = "{}";

    public string? CurrentAgent { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    private AnalysisSession()
    {
    }

    public static AnalysisSession Create()
    {
        var now = DateTimeOffset.UtcNow;

        return new AnalysisSession
        {
            Id = Guid.NewGuid(),
            Status = AnalysisSessionStatus.Pending,
            ContextJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void SetStatus(AnalysisSessionStatus status)
    {
        Status = status;
        UpdatedAt = DateTimeOffset.UtcNow;

        if (status is AnalysisSessionStatus.Completed or AnalysisSessionStatus.Failed)
        {
            CompletedAt = DateTimeOffset.UtcNow;
        }
    }

    public void SetCurrentAgent(string? currentAgent)
    {
        CurrentAgent = currentAgent;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetContext(string contextJson)
    {
        ContextJson = contextJson;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string reason)
    {
        Status = AnalysisSessionStatus.Failed;
        FailureReason = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
