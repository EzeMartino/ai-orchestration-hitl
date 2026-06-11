namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public sealed record FinancialDocumentMetadataCandidate(
    Guid Id,
    string FieldName,
    string Value,
    string SourceKind,
    decimal Confidence,
    int? SourcePage,
    string Evidence,
    string ExtractionStrategy,
    string ReviewState,
    string? InferenceExplanation);

public sealed record FinancialDocumentExtractionResult(
    FinancialDocumentMetadataCandidate Company,
    FinancialDocumentMetadataCandidate Currency,
    FinancialDocumentMetadataCandidate Unit,
    IReadOnlyList<FinancialMetricCandidate> Metrics);

public sealed record FinancialDocumentExtractionParseResult(
    bool Succeeded,
    FinancialDocumentExtractionResult? Result,
    string? FailureReason);

public sealed record FinancialDocumentExtractionRequest(
    string Markdown,
    int MaxEvidenceExcerptCharacters,
    int MaxMarkdownChunks);
