using System.Text.Json;

namespace Orchestration.Domain.FinancialMetricsExtraction;

public class FinancialMetricsExtractionDraft
{
    public const int OriginalFileNameMaxLength = 260;

    public const int ContentHashMaxLength = 128;

    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }

    public Guid UserId { get; private set; }

    public FinancialMetricsExtractionDraftStatus Status { get; private set; }

    public string OriginalFileName { get; private set; } = string.Empty;

    public long FileSizeBytes { get; private set; }

    public string ContentHash { get; private set; } = string.Empty;

    public string PayloadJson { get; private set; } = string.Empty;

    public Guid? ReviewedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    private FinancialMetricsExtractionDraft()
    {
    }

    public static FinancialMetricsExtractionDraft Create(
        Guid sessionId,
        Guid userId,
        string originalFileName,
        long fileSizeBytes,
        string contentHash,
        string payloadJson,
        DateTimeOffset now)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }

        var normalizedFileName = ValidateAndNormalizeFileName(originalFileName);

        if (fileSizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fileSizeBytes),
                fileSizeBytes,
                "File size cannot be negative.");
        }

        ValidateContentHash(contentHash);
        ValidatePayloadJson(payloadJson);

        if (now == default)
        {
            throw new ArgumentOutOfRangeException(nameof(now), "Timestamp cannot be the default value.");
        }

        return new FinancialMetricsExtractionDraft
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            UserId = userId,
            Status = FinancialMetricsExtractionDraftStatus.PendingReview,
            OriginalFileName = normalizedFileName,
            FileSizeBytes = fileSizeBytes,
            ContentHash = contentHash,
            PayloadJson = payloadJson,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void UpdatePayload(string payloadJson, DateTimeOffset now)
    {
        if (Status != FinancialMetricsExtractionDraftStatus.PendingReview)
        {
            throw new InvalidOperationException("Only pending review drafts can be edited.");
        }

        ValidatePayloadJson(payloadJson);
        ValidateLifecycleTimestamp(now);

        PayloadJson = payloadJson;
        UpdatedAt = now;
    }

    public void Confirm(Guid reviewerId, DateTimeOffset now)
    {
        ValidateReviewerId(reviewerId);
        ValidateLifecycleTimestamp(now);

        if (Status == FinancialMetricsExtractionDraftStatus.Confirmed)
        {
            return;
        }

        if (Status == FinancialMetricsExtractionDraftStatus.Discarded)
        {
            throw new InvalidOperationException("A discarded draft cannot be confirmed.");
        }

        Complete(FinancialMetricsExtractionDraftStatus.Confirmed, reviewerId, now);
    }

    public void Discard(Guid reviewerId, DateTimeOffset now)
    {
        ValidateReviewerId(reviewerId);
        ValidateLifecycleTimestamp(now);

        if (Status == FinancialMetricsExtractionDraftStatus.Discarded)
        {
            return;
        }

        if (Status == FinancialMetricsExtractionDraftStatus.Confirmed)
        {
            throw new InvalidOperationException("A confirmed draft cannot be discarded.");
        }

        Complete(FinancialMetricsExtractionDraftStatus.Discarded, reviewerId, now);
    }

    private void Complete(
        FinancialMetricsExtractionDraftStatus status,
        Guid reviewerId,
        DateTimeOffset now)
    {
        Status = status;
        ReviewedByUserId = reviewerId;
        UpdatedAt = now;
        CompletedAt = now;
    }

    private static void ValidateReviewerId(Guid reviewerId)
    {
        if (reviewerId == Guid.Empty)
        {
            throw new ArgumentException("Reviewer ID cannot be empty.", nameof(reviewerId));
        }
    }

    private void ValidateLifecycleTimestamp(DateTimeOffset now)
    {
        if (now == default || now < CreatedAt || now < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                now,
                "Timestamp cannot be earlier than the draft lifecycle.");
        }
    }

    private static string ValidateAndNormalizeFileName(string originalFileName)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw new ArgumentException("Original file name cannot be blank.", nameof(originalFileName));
        }

        var normalizedFileName = originalFileName.Trim();

        if (normalizedFileName.Length > OriginalFileNameMaxLength)
        {
            throw new ArgumentException(
                $"Original file name cannot exceed {OriginalFileNameMaxLength} characters.",
                nameof(originalFileName));
        }

        if (normalizedFileName is "." or ".."
            || Path.IsPathRooted(normalizedFileName)
            || normalizedFileName.Contains('/')
            || normalizedFileName.Contains('\\'))
        {
            throw new ArgumentException(
                "Original file name must be a safe base name without path components.",
                nameof(originalFileName));
        }

        return normalizedFileName;
    }

    private static void ValidateContentHash(string contentHash)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
        {
            throw new ArgumentException("Content hash cannot be blank.", nameof(contentHash));
        }

        if (contentHash.Length > ContentHashMaxLength)
        {
            throw new ArgumentException(
                $"Content hash cannot exceed {ContentHashMaxLength} characters.",
                nameof(contentHash));
        }
    }

    private static void ValidatePayloadJson(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw new ArgumentException("Payload JSON cannot be blank.", nameof(payloadJson));
        }

        try
        {
            using var _ = JsonDocument.Parse(payloadJson);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Payload must contain valid JSON.", nameof(payloadJson), exception);
        }
    }
}
