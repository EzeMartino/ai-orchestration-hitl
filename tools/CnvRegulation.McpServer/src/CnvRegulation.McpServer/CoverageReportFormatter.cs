using System.Text;
using CnvRegulation.Application.Contracts;

namespace CnvRegulation.McpServer;

/// <summary>
/// Formats regulation coverage diagnostics for console output.
/// </summary>
public static class CoverageReportFormatter
{
    /// <summary>
    /// Formats a coverage report.
    /// </summary>
    /// <param name="report">The coverage report.</param>
    /// <returns>The formatted report.</returns>
    public static string Format(RegulationCoverageReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();
        builder.AppendLine("Coverage report");
        builder.AppendLine();
        builder.AppendLine("Documents:");
        builder.AppendLine($"- Total: {report.DocumentsTotal}");
        AppendDistribution(builder, report.SourceDistribution);
        builder.AppendLine();
        builder.AppendLine("Chunks:");
        builder.AppendLine($"- Total: {report.ChunksTotal}");
        builder.AppendLine($"- With article: {report.ChunksWithArticle}");
        builder.AppendLine($"- Without article: {report.ChunksWithoutArticle}");
        builder.AppendLine($"- Distinct articles: {report.DistinctArticleCount}");
        builder.AppendLine($"- Duplicate URLs: {report.DuplicateUrlCount}");
        builder.AppendLine($"- Duplicates: {report.DuplicateChunkCount}");
        builder.AppendLine($"- Very short: {report.VeryShortChunks}");
        builder.AppendLine($"- Very long: {report.VeryLongChunks}");
        builder.AppendLine($"- Documents with zero chunks: {report.DocumentsWithZeroChunks}");
        builder.AppendLine($"- Possible wrappers: {report.PotentialWrapperDocuments}");
        builder.AppendLine();
        builder.AppendLine("Resolution numbers:");
        AppendDistribution(builder, report.ResolutionNumberDistribution);

        if (report.Warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Warnings:");
            foreach (var warning in report.Warnings)
            {
                builder.AppendLine($"- {warning}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendDistribution(
        StringBuilder builder,
        IReadOnlyList<CoverageDistributionItem> distribution)
    {
        if (distribution.Count == 0)
        {
            builder.AppendLine("- none: 0");
            return;
        }

        foreach (var item in distribution)
        {
            builder.AppendLine($"- {item.Label}: {item.Count}");
        }
    }
}
