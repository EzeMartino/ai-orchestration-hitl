using System.Globalization;
using System.Text;
using System.Text.Json;
using CnvRegulation.Application.Contracts;

namespace CnvRegulation.McpServer;

/// <summary>
/// Writes regulatory-analysis quality validation reports.
/// </summary>
public static class AnalysisQualityValidationReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// Writes a report as JSON or Markdown based on the output extension.
    /// </summary>
    /// <param name="report">The validation report.</param>
    /// <param name="outputPath">The output path.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The resolved output path.</returns>
    public static async Task<string> WriteAsync(
        AnalysisQualityValidationReport report,
        string outputPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var path = Path.GetFullPath(outputPath.Trim());
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            await File.WriteAllTextAsync(path, FormatMarkdown(report), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, report, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        return path;
    }

    private static string FormatMarkdown(AnalysisQualityValidationReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Analysis Quality Validation");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Cases: {report.Cases}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Passed: {report.Passed}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Failed: {report.Failed}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Hybrid search: {(report.UseHybridSearch ? "enabled" : "disabled")}");
        if (report.Warnings.Count > 0)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"- Warnings: {string.Join(", ", report.Warnings)}");
        }

        foreach (var result in report.Results)
        {
            builder.AppendLine();
            builder.AppendLine(CultureInfo.InvariantCulture, $"## {(result.Passed ? "PASS" : "FAIL")} {result.Id}");
            builder.AppendLine();
            builder.AppendLine(CultureInfo.InvariantCulture, $"- Input: {result.Text}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"- Regulation area: {result.RegulationArea ?? "(none)"}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"- Expected status: {result.ExpectedStatus}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"- Actual status: {result.ActualStatus}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"- Expected topics: {FormatList(result.ExpectedTopics)}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"- Detected topics: {FormatList(result.DetectedTopics)}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"- Findings: {result.FindingsCount}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"- Citations: {result.CitationsCount}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"- Risk levels: {FormatList(result.RiskLevels)}");
            if (result.Warnings.Count > 0)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- Warnings: {string.Join(", ", result.Warnings)}");
            }

            if (!result.Passed)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- Reason: {string.Join("; ", result.FailureReasons)}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatList(IReadOnlyList<string> values) =>
        values.Count == 0 ? "(none)" : string.Join(", ", values);
}
