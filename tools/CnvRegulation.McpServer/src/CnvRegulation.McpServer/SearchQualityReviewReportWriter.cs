using System.Globalization;
using System.Text;
using System.Text.Json;
using CnvRegulation.Application.Contracts;

namespace CnvRegulation.McpServer;

/// <summary>
/// Writes search-quality review reports.
/// </summary>
public static class SearchQualityReviewReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// Writes a review report as JSON or Markdown based on the output extension.
    /// </summary>
    /// <param name="report">The review report.</param>
    /// <param name="outputPath">The output path.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The resolved output path.</returns>
    public static async Task<string> WriteAsync(
        SearchQualityReviewReport report,
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

    private static string FormatMarkdown(SearchQualityReviewReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Search Quality Review");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Generated at: {report.GeneratedAt:O}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Queries: {report.Summary.Queries}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Full-text passed: {report.Summary.FullTextPassed}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Hybrid passed: {report.Summary.HybridPassed}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Top result changed: {report.Summary.TopResultChanged}");
        builder.AppendLine();

        foreach (var item in report.Items)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"## {item.QueryId}");
            builder.AppendLine();
            builder.AppendLine(CultureInfo.InvariantCulture, $"- Query: {item.Query}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"- Top changed: {(item.TopResultChanged ? "yes" : "no")}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"- Review decision: {item.ReviewDecision}");
            builder.AppendLine("- Review notes:");
            builder.AppendLine();
            builder.AppendLine("### Full-text");
            AppendMode(builder, item.FullText);
            builder.AppendLine();
            builder.AppendLine("### Hybrid");
            AppendMode(builder, item.Hybrid);
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendMode(StringBuilder builder, SearchQualityReviewModeResult mode)
    {
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Passed: {(mode.Passed ? "yes" : "no")}");
        if (mode.TopResult is null)
        {
            builder.AppendLine("- Top result: none");
            return;
        }

        builder.AppendLine(CultureInfo.InvariantCulture, $"- Source: {mode.TopResult.Source}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Title: {mode.TopResult.Title}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Article: {mode.TopResult.Article ?? "(none)"}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Score: {FormatScore(mode.TopResult.Score)}");
        if (mode.TopResult.ScoreBreakdown is not null)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"- Score breakdown: {FormatScoreBreakdown(mode.TopResult.ScoreBreakdown)}");
        }

        builder.AppendLine(CultureInfo.InvariantCulture, $"- Snippet: {mode.TopResult.Snippet ?? "(none)"}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Citation: {FormatCitation(mode.TopResult.Citation)}");
        if (mode.Warnings.Count > 0)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"- Warnings: {string.Join(", ", mode.Warnings)}");
        }
    }

    private static string FormatScore(double? score) =>
        score is null ? "n/a" : score.Value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatScoreBreakdown(IReadOnlyDictionary<string, double> scoreBreakdown) =>
        string.Join(", ", scoreBreakdown.Select(item =>
            string.Create(CultureInfo.InvariantCulture, $"{item.Key}={item.Value:0.###}")));

    private static string FormatCitation(SearchQualityReviewCitation? citation) =>
        citation is null
            ? "(none)"
            : string.Join(" | ", citation.Source, citation.Title, citation.Article ?? "(no article)", citation.Url);
}
