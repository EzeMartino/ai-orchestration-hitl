namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public sealed class StructuredFinancialMetricsFileUploadOptions
{
    public const string SectionName = "StructuredFinancialMetricsFileUpload";

    public long MaxFileSizeBytes { get; init; } = 10_485_760;

    public string[] AllowedExtensions { get; init; } =
    [
        ".json",
        ".csv",
        ".pdf"
    ];
}
