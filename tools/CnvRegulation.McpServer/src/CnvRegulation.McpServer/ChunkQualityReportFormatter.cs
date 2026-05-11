using System.Text;
using CnvRegulation.Application.Contracts;

namespace CnvRegulation.McpServer;

/// <summary>
/// Formats chunk quality diagnostics for console output.
/// </summary>
public static class ChunkQualityReportFormatter
{
    private const int MaxWarnings = 25;

    /// <summary>
    /// Formats a chunk quality report.
    /// </summary>
    /// <param name="report">The quality report.</param>
    /// <returns>The formatted report.</returns>
    public static string Format(ChunkQualityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();
        builder.AppendLine($"Documents analyzed: {report.DocumentsAnalyzed}");
        builder.AppendLine($"Chunks analyzed: {report.ChunksAnalyzed}");
        builder.AppendLine();
        builder.AppendLine("Quality summary:");
        builder.AppendLine($"- Empty chunks: {report.EmptyChunks}");
        builder.AppendLine($"- Very short chunks: {report.VeryShortChunks}");
        builder.AppendLine($"- Very long chunks: {report.VeryLongChunks}");
        builder.AppendLine($"- Chunks without article: {report.ChunksWithoutArticle}");
        builder.AppendLine($"- Possible duplicate chunks: {report.PossibleDuplicateChunks}");
        builder.AppendLine($"- Suspicious header/footer pollution: {report.SuspiciousHeaderFooterPollution}");
        builder.AppendLine($"- Average chunk length: {report.AverageChunkLength:F0} chars");
        builder.AppendLine($"- Median chunk length: {report.MedianChunkLength:F0} chars");

        if (report.Warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Top warnings:");

            foreach (var warning in report.Warnings.Take(MaxWarnings))
            {
                builder.AppendLine(FormatWarning(warning));
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatWarning(ChunkQualityWarning warning)
    {
        var label = string.IsNullOrWhiteSpace(warning.Article)
            ? warning.ChunkId
            : warning.Article;

        return $"[{warning.DocumentId}] {label} - {warning.Message}";
    }
}
