using System.Text;
using CnvRegulation.Application.Contracts;

namespace CnvRegulation.McpServer;

/// <summary>
/// Formats regulatory-analysis quality reports for console output.
/// </summary>
public static class AnalysisQualityValidationReportFormatter
{
    /// <summary>
    /// Formats a regulatory-analysis quality validation report.
    /// </summary>
    /// <param name="report">The validation report.</param>
    /// <returns>The formatted report.</returns>
    public static string Format(AnalysisQualityValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();
        builder.AppendLine("Analysis quality validation");
        builder.AppendLine();
        builder.AppendLine($"Cases: {report.Cases}");
        builder.AppendLine($"Passed: {report.Passed}");
        builder.AppendLine($"Failed: {report.Failed}");
        builder.AppendLine($"Hybrid search: {(report.UseHybridSearch ? "enabled" : "disabled")}");
        AppendWarnings(builder, report.Warnings);

        foreach (var result in report.Results)
        {
            builder.AppendLine();
            builder.AppendLine($"[{(result.Passed ? "PASS" : "FAIL")}] {result.Id}");
            builder.AppendLine($"Input: {result.Text}");
            builder.AppendLine($"Regulation area: {result.RegulationArea ?? "(none)"}");
            builder.AppendLine($"Expected status: {result.ExpectedStatus}");
            builder.AppendLine($"Actual status: {result.ActualStatus}");
            builder.AppendLine($"Expected topics: {FormatList(result.ExpectedTopics)}");
            builder.AppendLine($"Detected topics: {FormatList(result.DetectedTopics)}");
            builder.AppendLine($"Findings: {result.FindingsCount}");
            builder.AppendLine($"Citations: {result.CitationsCount}");
            builder.AppendLine($"Risk levels: {FormatList(result.RiskLevels)}");
            AppendWarnings(builder, result.Warnings);

            if (!result.Passed)
            {
                builder.AppendLine($"Reason: {string.Join("; ", result.FailureReasons)}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendWarnings(StringBuilder builder, IReadOnlyList<string> warnings)
    {
        if (warnings.Count > 0)
        {
            builder.AppendLine($"Warnings: {string.Join(", ", warnings)}");
        }
    }

    private static string FormatList(IReadOnlyList<string> values) =>
        values.Count == 0 ? "(none)" : string.Join(", ", values);
}
