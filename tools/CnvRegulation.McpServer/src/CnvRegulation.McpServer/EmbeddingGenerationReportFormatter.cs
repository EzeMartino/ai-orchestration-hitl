using System.Globalization;
using System.Text;
using CnvRegulation.Application.Contracts;

namespace CnvRegulation.McpServer;

/// <summary>
/// Formats embedding generation output for CLI use.
/// </summary>
public static class EmbeddingGenerationReportFormatter
{
    /// <summary>
    /// Formats a generation response.
    /// </summary>
    /// <param name="response">The response to format.</param>
    /// <returns>Human-readable text.</returns>
    public static string Format(GenerateEmbeddingsResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var builder = new StringBuilder();
        builder.AppendLine("Embedding generation");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Chunks scanned: {response.ChunksScanned}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Missing embeddings: {response.MissingEmbeddings}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Eligible chunks: {response.EligibleChunks}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Already embedded: {response.AlreadyEmbedded}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Generated: {response.Generated}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Failed: {response.Failed}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Skipped duplicates: {response.SkippedDuplicates}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Skipped non-searchable: {response.SkippedNonSearchable}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Skipped empty: {response.SkippedEmpty}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Estimated tokens: {response.EstimatedTokenCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Actual tokens: {FormatActualTokenCount(response.ActualTokenCount)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Provider: {response.Provider}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Model: {response.Model}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Dimensions: {response.Dimensions}");

        foreach (var warning in response.Warnings)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"Warning: {warning}");
        }

        return builder.ToString();
    }

    private static string FormatActualTokenCount(int? tokenCount) =>
        tokenCount is null ? "n/a" : tokenCount.Value.ToString(CultureInfo.InvariantCulture);
}
