using System.Text;
using CnvRegulation.Application.Contracts;

namespace CnvRegulation.McpServer;

/// <summary>
/// Formats search-quality mode comparison reports for console output.
/// </summary>
public static class SearchQualityComparisonReportFormatter
{
    /// <summary>
    /// Formats a comparison report.
    /// </summary>
    /// <param name="report">The comparison report.</param>
    /// <returns>The formatted report.</returns>
    public static string Format(SearchQualityComparisonReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();
        builder.AppendLine("Search quality comparison");
        builder.AppendLine();
        builder.AppendLine($"Queries: {report.Queries}");
        builder.AppendLine($"{report.BaselineMode}: {report.BaselinePassed} passed, {report.BaselineFailed} failed");
        builder.AppendLine($"{report.CandidateMode}: {report.CandidatePassed} passed, {report.CandidateFailed} failed");
        AppendWarnings(builder, report.Warnings);

        foreach (var result in report.Results)
        {
            builder.AppendLine();
            builder.AppendLine($"[{FormatStatus(result)}] {result.Id}");
            builder.AppendLine($"Query: {result.Query}");
            builder.AppendLine($"{report.BaselineMode} top: {result.BaselineTopResult}");
            builder.AppendLine($"{report.CandidateMode} top: {result.CandidateTopResult}");
            builder.AppendLine($"Top changed: {(result.TopResultChanged ? "yes" : "no")}");

            if (!string.IsNullOrWhiteSpace(result.CandidateScoreBreakdown))
            {
                builder.AppendLine($"Score breakdown: {result.CandidateScoreBreakdown}");
            }

            if (result.FailureReasons.Count > 0)
            {
                builder.AppendLine($"Reason: {string.Join("; ", result.FailureReasons)}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatStatus(SearchQualityComparisonResult result)
    {
        if (result.BaselinePassed && result.CandidatePassed)
        {
            return "PASS/PASS";
        }

        if (result.BaselinePassed)
        {
            return "PASS/FAIL";
        }

        return result.CandidatePassed ? "FAIL/PASS" : "FAIL/FAIL";
    }

    private static void AppendWarnings(StringBuilder builder, IReadOnlyList<string> warnings)
    {
        if (warnings.Count > 0)
        {
            builder.AppendLine($"Warnings: {string.Join(", ", warnings)}");
        }
    }
}
