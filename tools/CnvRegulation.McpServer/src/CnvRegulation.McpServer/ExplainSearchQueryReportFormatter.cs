using System.Globalization;
using System.Text;
using CnvRegulation.Application.Contracts;

namespace CnvRegulation.McpServer;

/// <summary>
/// Formats search query diagnostics for CLI output.
/// </summary>
public static class ExplainSearchQueryReportFormatter
{
    /// <summary>
    /// Formats a query explanation report.
    /// </summary>
    /// <param name="report">The report to format.</param>
    /// <returns>Human-readable CLI output.</returns>
    public static string Format(ExplainSearchQueryReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();
        builder.AppendLine("Query explanation");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Original query: {report.OriginalQuery}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Normalized query: {report.NormalizedQuery}");
        builder.AppendLine();
        AppendList(builder, "Expanded terms:", report.ExpandedTerms);
        builder.AppendLine("Generated search queries:");
        for (var index = 0; index < report.GeneratedQueries.Count; index++)
        {
            var generatedQuery = report.GeneratedQueries[index];
            builder.AppendLine(CultureInfo.InvariantCulture, $"{index + 1}. {generatedQuery.Query}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"   Raw results: {generatedQuery.RawResultCount}");
        }

        if (report.GeneratedQueries.Count == 0)
        {
            builder.AppendLine("- none");
        }

        builder.AppendLine();
        builder.AppendLine("Term presence:");
        foreach (var term in report.TermPresence)
        {
            var exact = term.ExactPhraseFound ? "yes" : "no";
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"- {term.Term}: chunks={term.FoundInChunks}, documents={term.FoundInDocuments}, exact={exact}");
        }

        if (report.TermPresence.Count == 0)
        {
            builder.AppendLine("- none");
        }

        builder.AppendLine();
        builder.AppendLine("Duplicate/wrapper filtering:");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Hidden duplicates: {report.HiddenDuplicateResults}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Hidden non-searchable: {report.HiddenNonSearchableResults}");
        builder.AppendLine();
        AppendList(builder, "Filters applied:", report.FiltersApplied);
        builder.AppendLine("Top partial matches:");
        foreach (var match in report.TopPartialMatches)
        {
            var article = string.IsNullOrWhiteSpace(match.Article) ? "no article" : match.Article;
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"- {match.Source} | {match.Title} | {article} | score={match.Score:0.##} | {match.Snippet}");
        }

        if (report.TopPartialMatches.Count == 0)
        {
            builder.AppendLine("- none");
        }

        builder.AppendLine();
        AppendList(builder, "Warnings:", report.Warnings);
        builder.AppendLine(CultureInfo.InvariantCulture, $"Recommendation: {report.Recommendation}");

        return builder.ToString();
    }

    private static void AppendList(StringBuilder builder, string title, IReadOnlyList<string> values)
    {
        builder.AppendLine(title);
        if (values.Count == 0)
        {
            builder.AppendLine("- none");
            builder.AppendLine();
            return;
        }

        foreach (var value in values)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"- {value}");
        }

        builder.AppendLine();
    }
}
