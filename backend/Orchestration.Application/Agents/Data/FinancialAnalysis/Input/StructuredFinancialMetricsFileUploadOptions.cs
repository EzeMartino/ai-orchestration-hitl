namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsFileUploadOptions
{
    public const string SectionName = "StructuredFinancialMetricsFileUpload";

    public long MaxFileSizeBytes { get; init; } = 20_971_520;

    public string[] AllowedExtensions { get; init; } =
    [
        ".json",
        ".csv",
        ".pdf"
    ];
}
