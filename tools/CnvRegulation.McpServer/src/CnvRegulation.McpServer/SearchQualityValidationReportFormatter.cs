using System.Text;
using CnvRegulation.Application.Contracts;

namespace CnvRegulation.McpServer;

/// <summary>
/// Formats search-quality validation reports for console output.
/// </summary>
public static class SearchQualityValidationReportFormatter
{
    /// <summary>
    /// Formats a search-quality validation report.
    /// </summary>
    /// <param name="report">The validation report.</param>
    /// <returns>The formatted report.</returns>
    public static string Format(SearchQualityValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();
        builder.AppendLine("Search quality validation");
        builder.AppendLine();
        builder.AppendLine($"Queries: {report.Queries}");
        builder.AppendLine($"Passed: {report.Passed}");
        builder.AppendLine($"Failed: {report.Failed}");
        AppendWarnings(builder, report.Warnings);

        foreach (var result in report.Results)
        {
            builder.AppendLine();
            builder.AppendLine($"[{(result.Passed ? "PASS" : "FAIL")}] {result.Id}");
            builder.AppendLine($"Query: {result.Query}");
            builder.AppendLine($"Expanded: {FormatList(result.ExpandedQueries)}");
            builder.AppendLine($"Results: {result.ResultCount}");
            builder.AppendLine($"Top: {FormatTopResult(result)}");
            builder.AppendLine($"Citations: {(result.CitationsPresent ? "yes" : "no")}");
            AppendWarnings(builder, result.Warnings);

            if (!result.Passed)
            {
                builder.AppendLine($"Reason: {string.Join("; ", result.FailureReasons)}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatTopResult(SearchQualityValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(result.TopResultSource))
        {
            return "(none)";
        }

        var article = string.IsNullOrWhiteSpace(result.TopResultArticle)
            ? "(no article)"
            : result.TopResultArticle;
        var score = result.TopResultScore is null ? "n/a" : result.TopResultScore.Value.ToString("F3");

        return $"{result.TopResultSource} | {result.TopResultTitle} | {article} | {score}";
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
