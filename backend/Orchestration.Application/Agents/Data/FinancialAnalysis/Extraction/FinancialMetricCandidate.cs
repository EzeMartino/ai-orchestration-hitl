namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public static class FinancialMetricCandidateSourceKinds
{
    public const string Reported = "reported";
    public const string Inferred = "inferred";
    public const string Computed = "computed";
    public const string HumanCorrected = "human_corrected";
}

public static class FinancialMetricCandidateReviewStates
{
    public const string Explicit = "explicit";
    public const string Inferred = "inferred";
    public const string Conflict = "conflict";
    public const string Missing = "missing";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string HumanCorrected = "human_corrected";
}

public sealed record FinancialMetricCandidate(
    Guid Id,
    string Name,
    string Period,
    decimal? Value,
    string? Currency,
    string? Unit,
    string SourceKind,
    decimal Confidence,
    int? SourcePage,
    string Evidence,
    string ExtractionStrategy,
    string ReviewState,
    string? InferenceExplanation);
