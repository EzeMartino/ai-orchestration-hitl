namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public sealed record FinancialMetricsExtractionDraftPayload(
    int SchemaVersion,
    StructuredFinancialMetricsInput ProposedInput,
    IReadOnlyList<FinancialMetricCandidate> Candidates,
    IReadOnlyList<FinancialMetricCandidateConflict> Conflicts,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<string> FallbackReasons,
    IReadOnlyList<FinancialMetricsValidationIssue> ValidationIssues,
    FinancialMetricsExtractionDiagnostics Diagnostics)
{
    public const int CurrentSchemaVersion = 1;

    public IReadOnlyList<FinancialDocumentMetadataCandidate> MetadataCandidates
    {
        get;
        init;
    } = [];
}

public sealed record FinancialMetricsExtractionDiagnostics
{
    public bool NativeTextAvailable { get; init; }

    public bool OcrAttempted { get; init; }

    public bool OcrSucceeded { get; init; }

    public bool MarkItDownAttempted { get; init; }

    public bool MarkItDownSucceeded { get; init; }

    public bool SemanticAttempted { get; init; }

    public bool SemanticSucceeded { get; init; }

    public int? PageCount { get; init; }

    public int? MarkdownCharacterCount { get; init; }

    public int? CandidateCount { get; init; }

    public int? ConflictCount { get; init; }

    public long? NativeTextDurationMilliseconds { get; init; }

    public long? OcrDurationMilliseconds { get; init; }

    public long? MarkItDownDurationMilliseconds { get; init; }

    public long? SemanticDurationMilliseconds { get; init; }

    public long? TotalDurationMilliseconds { get; init; }

    public IReadOnlyList<string> ReasonCodes { get; init; } = [];
}

public static class FinancialMetricCandidateReviewDecisions
{
    public const string Accepted = FinancialMetricCandidateReviewStates.Accepted;

    public const string Rejected = FinancialMetricCandidateReviewStates.Rejected;

    public const string HumanCorrected =
        FinancialMetricCandidateReviewStates.HumanCorrected;
}

public sealed record FinancialMetricCandidateReviewUpdate(
    Guid CandidateId,
    string Decision,
    decimal? Value,
    string? Currency,
    string? Unit,
    string? MetadataValue);

public sealed record CreateFinancialMetricsExtractionDraftRequest(
    string OriginalFileName,
    long FileSizeBytes,
    string ContentHash,
    FinancialMetricsExtractionDraftPayload Payload);

public sealed record UpdateFinancialMetricsExtractionDraftRequest(
    IReadOnlyList<FinancialMetricCandidateReviewUpdate> Candidates,
    StructuredFinancialMetricsInput ProposedInput);

public sealed record FinancialMetricsExtractionDraftDto(
    Guid Id,
    Guid SessionId,
    string Status,
    string OriginalFileName,
    long FileSizeBytes,
    string ContentHash,
    FinancialMetricsExtractionDraftPayload Payload,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

public enum FinancialMetricsExtractionDraftResultKind
{
    Success,
    NotFound,
    Invalid,
    Conflict
}

public sealed record FinancialMetricsExtractionDraftServiceResult(
    FinancialMetricsExtractionDraftResultKind Kind,
    FinancialMetricsExtractionDraftDto? Draft,
    IReadOnlyList<string> Errors,
    IReadOnlyList<FinancialMetricsValidationIssue> ValidationIssues)
{
    public static FinancialMetricsExtractionDraftServiceResult Success(
        FinancialMetricsExtractionDraftDto draft)
    {
        return new FinancialMetricsExtractionDraftServiceResult(
            FinancialMetricsExtractionDraftResultKind.Success,
            draft,
            [],
            []);
    }

    public static FinancialMetricsExtractionDraftServiceResult NotFound(
        string error)
    {
        return Failure(
            FinancialMetricsExtractionDraftResultKind.NotFound,
            error);
    }

    public static FinancialMetricsExtractionDraftServiceResult Invalid(
        string error,
        IReadOnlyList<FinancialMetricsValidationIssue>? validationIssues = null)
    {
        return Failure(
            FinancialMetricsExtractionDraftResultKind.Invalid,
            error,
            validationIssues);
    }

    public static FinancialMetricsExtractionDraftServiceResult Conflict(
        string error)
    {
        return Failure(
            FinancialMetricsExtractionDraftResultKind.Conflict,
            error);
    }

    private static FinancialMetricsExtractionDraftServiceResult Failure(
        FinancialMetricsExtractionDraftResultKind kind,
        string error,
        IReadOnlyList<FinancialMetricsValidationIssue>? validationIssues = null)
    {
        return new FinancialMetricsExtractionDraftServiceResult(
            kind,
            null,
            [error],
            validationIssues ?? []);
    }
}
