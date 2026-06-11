namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed record StructuredFinancialMetricsPdfExtractionRequest(
    string? DocumentId,
    string? Company,
    string? Currency,
    string? Unit,
    string? OriginalFileName
);

public sealed record StructuredFinancialMetricsTextParseRequest(
    string DocumentId,
    string? Company,
    string? Currency,
    string? Unit,
    IReadOnlyList<StructuredFinancialMetricsExtractedPage> Pages,
    string Source
);

public sealed record StructuredFinancialMetricsExtractedPage(
    int PageNumber,
    string Text,
    decimal? OcrConfidence = null
);

public sealed record StructuredFinancialMetricsPdfExtractionResult(
    bool IsValid,
    StructuredFinancialMetricsInput? Input,
    IReadOnlyList<FinancialMetricsValidationIssue> Errors,
    IReadOnlyList<FinancialMetricsValidationIssue> Warnings,
    bool UsedOcr,
    bool NativeTextAvailable = false
);
