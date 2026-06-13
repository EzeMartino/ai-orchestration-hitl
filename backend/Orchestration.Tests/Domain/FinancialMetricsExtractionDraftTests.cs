using Orchestration.Domain.FinancialMetricsExtraction;

namespace Orchestration.Tests.Domain;

public class FinancialMetricsExtractionDraftTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 6, 13, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ValidInput_InitializesPendingReviewDraft()
    {
        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 6, 13, 12, 30, 0, TimeSpan.Zero);
        const string payloadJson = """{"metrics":[]}""";

        var draft = FinancialMetricsExtractionDraft.Create(
            sessionId,
            userId,
            "financial-report.pdf",
            42_000,
            "sha256:abc123",
            payloadJson,
            now);

        Assert.NotEqual(Guid.Empty, draft.Id);
        Assert.Equal(sessionId, draft.SessionId);
        Assert.Equal(userId, draft.UserId);
        Assert.Equal(FinancialMetricsExtractionDraftStatus.PendingReview, draft.Status);
        Assert.Equal("financial-report.pdf", draft.OriginalFileName);
        Assert.Equal(42_000, draft.FileSizeBytes);
        Assert.Equal("sha256:abc123", draft.ContentHash);
        Assert.Equal(payloadJson, draft.PayloadJson);
        Assert.Null(draft.ReviewedByUserId);
        Assert.Equal(now, draft.CreatedAt);
        Assert.Equal(now, draft.UpdatedAt);
        Assert.Null(draft.CompletedAt);
    }

    [Fact]
    public void Create_EmptySessionId_ThrowsArgumentException()
    {
        var action = () => CreateDraft(sessionId: Guid.Empty);

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Create_EmptyUserId_ThrowsArgumentException()
    {
        var action = () => CreateDraft(userId: Guid.Empty);

        Assert.Throws<ArgumentException>(action);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../financial-report.pdf")]
    [InlineData(@"..\financial-report.pdf")]
    [InlineData("folder/financial-report.pdf")]
    [InlineData(@"folder\financial-report.pdf")]
    [InlineData("/financial-report.pdf")]
    [InlineData(@"C:\financial-report.pdf")]
    public void Create_UnsafeOriginalFileName_ThrowsArgumentException(string? originalFileName)
    {
        var action = () => CreateDraft(originalFileName: originalFileName!);

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Create_OriginalFileNameExceedsMaximumLength_ThrowsArgumentException()
    {
        var action = () => CreateDraft(originalFileName: new string('a', 261));

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Create_OriginalFileNameHasOuterWhitespace_TrimsFileName()
    {
        var draft = CreateDraft(originalFileName: "  financial-report.pdf  ");

        Assert.Equal("financial-report.pdf", draft.OriginalFileName);
    }

    [Fact]
    public void Create_NegativeFileSize_ThrowsArgumentOutOfRangeException()
    {
        var action = () => CreateDraft(fileSizeBytes: -1);

        Assert.Throws<ArgumentOutOfRangeException>(action);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankContentHash_ThrowsArgumentException(string? contentHash)
    {
        var action = () => CreateDraft(contentHash: contentHash!);

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Create_ContentHashExceedsMaximumLength_ThrowsArgumentException()
    {
        var action = () => CreateDraft(contentHash: new string('a', 129));

        Assert.Throws<ArgumentException>(action);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("""{"metrics":}""")]
    public void Create_InvalidPayloadJson_ThrowsArgumentException(string? payloadJson)
    {
        var action = () => CreateDraft(payloadJson: payloadJson!);

        Assert.Throws<ArgumentException>(action);
    }

    [Theory]
    [InlineData("""{"metrics":[]}""")]
    [InlineData("""[{"name":"revenue"}]""")]
    public void Create_ValidPayloadJson_PreservesOriginalText(string payloadJson)
    {
        var draft = CreateDraft(payloadJson: payloadJson);

        Assert.Equal(payloadJson, draft.PayloadJson);
    }

    [Fact]
    public void Create_DefaultTimestamp_ThrowsArgumentOutOfRangeException()
    {
        var action = () => CreateDraft(now: DateTimeOffset.MinValue);

        Assert.Throws<ArgumentOutOfRangeException>(action);
    }

    [Fact]
    public void UpdatePayload_PendingDraft_UpdatesPayloadAndTimestamp()
    {
        var draft = CreateDraft();
        var updatedAt = CreatedAt.AddMinutes(5);
        const string updatedPayload = """[{"name":"revenue","value":100}]""";

        draft.UpdatePayload(updatedPayload, updatedAt);

        Assert.Equal(updatedPayload, draft.PayloadJson);
        Assert.Equal(updatedAt, draft.UpdatedAt);
        Assert.Equal(FinancialMetricsExtractionDraftStatus.PendingReview, draft.Status);
        Assert.Null(draft.ReviewedByUserId);
        Assert.Null(draft.CompletedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    public void UpdatePayload_InvalidJson_ThrowsArgumentException(string payloadJson)
    {
        var draft = CreateDraft();

        var action = () => draft.UpdatePayload(payloadJson, CreatedAt.AddMinutes(1));

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void UpdatePayload_TimestampBeforeUpdatedAt_ThrowsArgumentOutOfRangeException()
    {
        var draft = CreateDraft();
        draft.UpdatePayload("""{"version":2}""", CreatedAt.AddMinutes(2));

        var action = () => draft.UpdatePayload("""{"version":3}""", CreatedAt.AddMinutes(1));

        Assert.Throws<ArgumentOutOfRangeException>(action);
    }

    [Fact]
    public void UpdatePayload_DefaultTimestamp_ThrowsArgumentOutOfRangeException()
    {
        var draft = CreateDraft();

        var action = () => draft.UpdatePayload("""{"version":2}""", DateTimeOffset.MinValue);

        Assert.Throws<ArgumentOutOfRangeException>(action);
    }

    [Theory]
    [InlineData(FinancialMetricsExtractionDraftStatus.Confirmed)]
    [InlineData(FinancialMetricsExtractionDraftStatus.Discarded)]
    public void UpdatePayload_TerminalDraft_ThrowsInvalidOperationException(
        FinancialMetricsExtractionDraftStatus terminalStatus)
    {
        var draft = CreateDraft();
        var reviewerId = Guid.NewGuid();

        if (terminalStatus == FinancialMetricsExtractionDraftStatus.Confirmed)
        {
            draft.Confirm(reviewerId, CreatedAt.AddMinutes(1));
        }
        else
        {
            draft.Discard(reviewerId, CreatedAt.AddMinutes(1));
        }

        var action = () => draft.UpdatePayload("""{"version":2}""", CreatedAt.AddMinutes(2));

        Assert.Throws<InvalidOperationException>(action);
    }

    [Fact]
    public void Confirm_PendingDraft_RecordsReviewerAndCompletion()
    {
        var draft = CreateDraft();
        var reviewerId = Guid.NewGuid();
        var completedAt = CreatedAt.AddMinutes(10);

        draft.Confirm(reviewerId, completedAt);

        Assert.Equal(FinancialMetricsExtractionDraftStatus.Confirmed, draft.Status);
        Assert.Equal(reviewerId, draft.ReviewedByUserId);
        Assert.Equal(completedAt, draft.UpdatedAt);
        Assert.Equal(completedAt, draft.CompletedAt);
    }

    [Fact]
    public void Confirm_EmptyReviewerId_ThrowsArgumentException()
    {
        var draft = CreateDraft();

        var action = () => draft.Confirm(Guid.Empty, CreatedAt.AddMinutes(1));

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Confirm_TimestampBeforeUpdatedAt_ThrowsArgumentOutOfRangeException()
    {
        var draft = CreateDraft();
        draft.UpdatePayload("""{"version":2}""", CreatedAt.AddMinutes(2));

        var action = () => draft.Confirm(Guid.NewGuid(), CreatedAt.AddMinutes(1));

        Assert.Throws<ArgumentOutOfRangeException>(action);
    }

    [Fact]
    public void Confirm_ConfirmedDraft_IsIdempotentAndPreservesOriginalAudit()
    {
        var draft = CreateDraft();
        var originalReviewerId = Guid.NewGuid();
        var originalCompletedAt = CreatedAt.AddMinutes(1);
        draft.Confirm(originalReviewerId, originalCompletedAt);

        draft.Confirm(Guid.NewGuid(), originalCompletedAt.AddMinutes(5));

        Assert.Equal(FinancialMetricsExtractionDraftStatus.Confirmed, draft.Status);
        Assert.Equal(originalReviewerId, draft.ReviewedByUserId);
        Assert.Equal(originalCompletedAt, draft.UpdatedAt);
        Assert.Equal(originalCompletedAt, draft.CompletedAt);
    }

    [Fact]
    public void Confirm_DiscardedDraft_ThrowsInvalidOperationException()
    {
        var draft = CreateDraft();
        draft.Discard(Guid.NewGuid(), CreatedAt.AddMinutes(1));

        var action = () => draft.Confirm(Guid.NewGuid(), CreatedAt.AddMinutes(2));

        Assert.Throws<InvalidOperationException>(action);
    }

    [Fact]
    public void Discard_PendingDraft_RecordsReviewerAndCompletion()
    {
        var draft = CreateDraft();
        var reviewerId = Guid.NewGuid();
        var completedAt = CreatedAt.AddMinutes(10);

        draft.Discard(reviewerId, completedAt);

        Assert.Equal(FinancialMetricsExtractionDraftStatus.Discarded, draft.Status);
        Assert.Equal(reviewerId, draft.ReviewedByUserId);
        Assert.Equal(completedAt, draft.UpdatedAt);
        Assert.Equal(completedAt, draft.CompletedAt);
    }

    [Fact]
    public void Discard_EmptyReviewerId_ThrowsArgumentException()
    {
        var draft = CreateDraft();

        var action = () => draft.Discard(Guid.Empty, CreatedAt.AddMinutes(1));

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Discard_TimestampBeforeUpdatedAt_ThrowsArgumentOutOfRangeException()
    {
        var draft = CreateDraft();
        draft.UpdatePayload("""{"version":2}""", CreatedAt.AddMinutes(2));

        var action = () => draft.Discard(Guid.NewGuid(), CreatedAt.AddMinutes(1));

        Assert.Throws<ArgumentOutOfRangeException>(action);
    }

    [Fact]
    public void Discard_DiscardedDraft_IsIdempotentAndPreservesOriginalAudit()
    {
        var draft = CreateDraft();
        var originalReviewerId = Guid.NewGuid();
        var originalCompletedAt = CreatedAt.AddMinutes(1);
        draft.Discard(originalReviewerId, originalCompletedAt);

        draft.Discard(Guid.NewGuid(), originalCompletedAt.AddMinutes(5));

        Assert.Equal(FinancialMetricsExtractionDraftStatus.Discarded, draft.Status);
        Assert.Equal(originalReviewerId, draft.ReviewedByUserId);
        Assert.Equal(originalCompletedAt, draft.UpdatedAt);
        Assert.Equal(originalCompletedAt, draft.CompletedAt);
    }

    [Fact]
    public void Discard_ConfirmedDraft_ThrowsInvalidOperationException()
    {
        var draft = CreateDraft();
        draft.Confirm(Guid.NewGuid(), CreatedAt.AddMinutes(1));

        var action = () => draft.Discard(Guid.NewGuid(), CreatedAt.AddMinutes(2));

        Assert.Throws<InvalidOperationException>(action);
    }

    private static FinancialMetricsExtractionDraft CreateDraft(
        Guid? sessionId = null,
        Guid? userId = null,
        string originalFileName = "financial-report.pdf",
        long fileSizeBytes = 42_000,
        string contentHash = "sha256:abc123",
        string payloadJson = """{"metrics":[]}""",
        DateTimeOffset? now = null)
    {
        return FinancialMetricsExtractionDraft.Create(
            sessionId ?? Guid.NewGuid(),
            userId ?? Guid.NewGuid(),
            originalFileName,
            fileSizeBytes,
            contentHash,
            payloadJson,
            now ?? CreatedAt);
    }
}
