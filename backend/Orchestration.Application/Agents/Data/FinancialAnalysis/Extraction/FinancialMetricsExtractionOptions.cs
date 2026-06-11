namespace Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;

public sealed class FinancialMetricsExtractionOptions
{
    public const string SectionName = "FinancialMetricsExtraction";

    public bool SemanticEnrichmentEnabled { get; init; } = false;

    public string Mode { get; init; } = "ReviewOnly";

    public decimal DeterministicCoverageThreshold { get; init; } = 0.7m;

    public decimal AutomaticAcceptanceConfidence { get; init; } = 0.9m;

    public int MaxMarkdownCharacters { get; init; } = 200_000;

    public int MaxMarkdownChunks { get; init; } = 12;

    public int ConversionTimeoutSeconds { get; init; } = 60;

    public int SemanticExtractionTimeoutSeconds { get; init; } = 90;

    public int MaxEvidenceExcerptCharacters { get; init; } = 500;
}
