using System.Text.Json;

namespace Orchestration.Domain.FinancialMetricsExtraction;

public class FinancialMetricsExtractionDraft
{
    private const string InvalidWindowsFileNameCharacters = "<>:\"/\\|?*";

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

        var normalizedNow = NormalizeTimestamp(now, nameof(now));

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
            CreatedAt = normalizedNow,
            UpdatedAt = normalizedNow
        };
    }

    public void UpdatePayload(string payloadJson, DateTimeOffset now)
    {
        if (Status != FinancialMetricsExtractionDraftStatus.PendingReview)
        {
            throw new InvalidOperationException("Only pending review drafts can be edited.");
        }

        ValidatePayloadJson(payloadJson);
        var normalizedNow = NormalizeLifecycleTimestamp(now);

        PayloadJson = payloadJson;
        UpdatedAt = normalizedNow;
    }

    public void Confirm(Guid reviewerId, DateTimeOffset now)
    {
        if (Status == FinancialMetricsExtractionDraftStatus.Confirmed)
        {
            return;
        }

        if (Status == FinancialMetricsExtractionDraftStatus.Discarded)
        {
            throw new InvalidOperationException("A discarded draft cannot be confirmed.");
        }

        ValidateReviewerId(reviewerId);
        var normalizedNow = NormalizeLifecycleTimestamp(now);

        Complete(FinancialMetricsExtractionDraftStatus.Confirmed, reviewerId, normalizedNow);
    }

    public void Discard(Guid reviewerId, DateTimeOffset now)
    {
        if (Status == FinancialMetricsExtractionDraftStatus.Discarded)
        {
            return;
        }

        if (Status == FinancialMetricsExtractionDraftStatus.Confirmed)
        {
            throw new InvalidOperationException("A confirmed draft cannot be discarded.");
        }

        ValidateReviewerId(reviewerId);
        var normalizedNow = NormalizeLifecycleTimestamp(now);

        Complete(FinancialMetricsExtractionDraftStatus.Discarded, reviewerId, normalizedNow);
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

    private DateTimeOffset NormalizeLifecycleTimestamp(DateTimeOffset now)
    {
        var normalizedNow = NormalizeTimestamp(now, nameof(now));

        if (normalizedNow < CreatedAt || normalizedNow < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                now,
                "Timestamp cannot be earlier than the draft lifecycle.");
        }

        return normalizedNow;
    }

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset timestamp, string parameterName)
    {
        if (timestamp == default)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                timestamp,
                "Timestamp cannot be the default value.");
        }

        return timestamp.ToUniversalTime();
    }

    private static string ValidateAndNormalizeFileName(string originalFileName)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw new ArgumentException("Original file name cannot be blank.", nameof(originalFileName));
        }

        var normalizedFileName = originalFileName.TrimStart(' ');

        if (normalizedFileName.Length > OriginalFileNameMaxLength)
        {
            throw new ArgumentException(
                $"Original file name cannot exceed {OriginalFileNameMaxLength} characters.",
                nameof(originalFileName));
        }

        if (normalizedFileName is "." or ".."
            || Path.IsPathRooted(normalizedFileName)
            || normalizedFileName.EndsWith('.')
            || normalizedFileName.EndsWith(' ')
            || ContainsInvalidWindowsFileNameCharacter(normalizedFileName)
            || IsReservedWindowsDeviceName(normalizedFileName))
        {
            throw new ArgumentException(
                "Original file name must be a Windows-safe base name.",
                nameof(originalFileName));
        }

        return normalizedFileName;
    }

    private static bool ContainsInvalidWindowsFileNameCharacter(string fileName)
    {
        foreach (var character in fileName)
        {
            if (character < 32 || InvalidWindowsFileNameCharacters.Contains(character))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReservedWindowsDeviceName(string fileName)
    {
        var extensionSeparatorIndex = fileName.IndexOf('.');
        var baseName = extensionSeparatorIndex >= 0
            ? fileName[..extensionSeparatorIndex]
            : fileName;

        if (baseName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return baseName.Length == 4
            && baseName[3] is >= '1' and <= '9'
            && (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
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
            using var document = JsonDocument.Parse(payloadJson);
            ValidatePayloadElement(document.RootElement, nameof(payloadJson));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Payload must contain valid JSON.", nameof(payloadJson), exception);
        }
    }

    private static void ValidatePayloadElement(JsonElement element, string parameterName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Contains('\0'))
                    {
                        throw new ArgumentException(
                            "Payload JSON cannot contain decoded NUL characters.",
                            parameterName);
                    }

                    ValidatePayloadElement(property.Value, parameterName);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ValidatePayloadElement(item, parameterName);
                }

                break;

            case JsonValueKind.String:
                if (element.GetString()?.Contains('\0') == true)
                {
                    throw new ArgumentException(
                        "Payload JSON cannot contain decoded NUL characters.",
                        parameterName);
                }

                break;

            case JsonValueKind.Number:
                if (!element.TryGetDecimal(out _))
                {
                    throw new ArgumentException(
                        "Payload JSON numbers must fit within the decimal range.",
                        parameterName);
                }

                break;
        }
    }
}
