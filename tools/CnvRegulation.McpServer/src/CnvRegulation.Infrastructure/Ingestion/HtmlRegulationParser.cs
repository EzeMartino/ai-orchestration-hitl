using System.Net;
using System.Text;
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

        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        var html = DecodeHtml(bytes);
        var withoutScripts = ScriptOrStyleRegex().Replace(html, " ");
        var withLineBreaks = BlockSeparatorRegex().Replace(withoutScripts, Environment.NewLine);
        var withoutTags = HtmlTagRegex().Replace(withLineBreaks, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);

        return PlainTextRegulationParser.NormalizeWhitespace(decoded);
    }

    private static string DecodeHtml(byte[] bytes)
    {
        var header = Encoding.ASCII.GetString(bytes.AsSpan(0, Math.Min(bytes.Length, 4096)));
        if (header.Contains("charset=ISO-8859-1", StringComparison.OrdinalIgnoreCase)
            || header.Contains("charset=latin1", StringComparison.OrdinalIgnoreCase)
            || header.Contains("charset=windows-1252", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.Latin1.GetString(bytes);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    [GeneratedRegex("<(script|style)[^>]*>.*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyleRegex();

    [GeneratedRegex("</?(p|div|br|section|article|h1|h2|h3|li|tr|table)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockSeparatorRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTagRegex();
}
