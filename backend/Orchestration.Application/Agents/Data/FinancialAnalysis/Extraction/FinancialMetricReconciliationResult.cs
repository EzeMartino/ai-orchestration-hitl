using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public sealed record FinancialMetricReconciliationResult(
    StructuredFinancialMetricsInput ProposedInput,
    IReadOnlyList<FinancialMetricCandidate> Candidates,
    IReadOnlyList<FinancialMetricCandidateConflict> Conflicts,
    IReadOnlyList<string> MissingFields,
    bool RequiresReview,
    bool CanAutoAccept)
{
    public IReadOnlyList<FinancialDocumentMetadataCandidate>
        MetadataCandidates { get; init; } = [];
}

public sealed record FinancialMetricCandidateConflict(
    string Kind,
    string FieldName,
    string? MetricName,
    string? Period,
    string? ProposedValue,
    IReadOnlyList<FinancialMetricCandidate> MetricCandidates,
    IReadOnlyList<FinancialDocumentMetadataCandidate> MetadataCandidates);
