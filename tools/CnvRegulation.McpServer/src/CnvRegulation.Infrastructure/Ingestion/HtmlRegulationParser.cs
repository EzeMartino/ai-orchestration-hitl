using System.Net;
using System.Text.RegularExpressions;

namespace CnvRegulation.Infrastructure.Ingestion;

/// <summary>
/// Parses local HTML regulation source files into plain text.
/// </summary>
public sealed partial class HtmlRegulationParser
{
    /// <summary>
    /// Reads and extracts text content from a local HTML file.
    /// </summary>
    /// <param name="filePath">The HTML file path.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The extracted text content.</returns>
    public async Task<string> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var html = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        var withoutScripts = ScriptOrStyleRegex().Replace(html, " ");
        var withLineBreaks = BlockSeparatorRegex().Replace(withoutScripts, Environment.NewLine);
        var withoutTags = HtmlTagRegex().Replace(withLineBreaks, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);

        return PlainTextRegulationParser.NormalizeWhitespace(decoded);
    }

    [GeneratedRegex("<(script|style)[^>]*>.*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyleRegex();

    [GeneratedRegex("</?(p|div|br|section|article|h1|h2|h3|li|tr|table)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockSeparatorRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTagRegex();
}
