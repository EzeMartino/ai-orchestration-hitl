using System.Text;
using CnvRegulation.Application.Contracts;

namespace CnvRegulation.McpServer;

/// <summary>
/// Formats source inspection diagnostics for console output.
/// </summary>
public static class SourceInspectionReportFormatter
{
    /// <summary>
    /// Formats an inspection response as a human-readable report.
    /// </summary>
    /// <param name="response">The inspection response.</param>
    /// <returns>The formatted report.</returns>
    public static string Format(InspectSourcesResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var builder = new StringBuilder();
        builder.AppendLine($"Documents inspected: {response.DocumentsInspected}");
        builder.AppendLine($"Documents with chunks: {response.DocumentsWithChunks}");
        builder.AppendLine($"Documents without chunks: {response.DocumentsWithoutChunks}");
        builder.AppendLine($"Unsupported files: {response.UnsupportedFiles}");

        AppendWarnings(builder, response.Warnings);

        foreach (var document in response.Documents)
        {
            builder.AppendLine();
            builder.AppendLine($"[{document.FileName}]");
            AppendOptionalLine(builder, "Source", document.Source);
            AppendOptionalLine(builder, "Title", document.Title);
            AppendOptionalLine(builder, "Document type", document.DocumentType);
            AppendOptionalLine(builder, "File type", document.FileType);
            if (document.PageCount is not null)
            {
                builder.AppendLine($"Pages: {document.PageCount}");
            }

            builder.AppendLine($"Extracted text length: {document.ExtractedTextLength}");
            builder.AppendLine($"Chunks: {document.ChunkCount}");
            builder.AppendLine($"Titles detected: {document.DetectedTitles.Count}");
            builder.AppendLine($"Chapters detected: {document.DetectedChapters.Count}");
            builder.AppendLine($"Sections detected: {document.DetectedSections.Count}");
            builder.AppendLine($"Articles detected: {document.DetectedArticles.Count}");
            builder.AppendLine($"First articles: {FormatList(document.FirstArticles)}");
            AppendWarnings(builder, document.Warnings);
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendOptionalLine(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine($"{label}: {value}");
        }
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
