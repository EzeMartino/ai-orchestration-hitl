using System.Text.RegularExpressions;
using CnvRegulation.Application.Abstractions;

namespace CnvRegulation.Infrastructure.Parsing;

/// <summary>
/// Normalizes text extracted from PDF sources before legal chunking.
/// </summary>
public sealed partial class PdfExtractedTextNormalizer : ITextNormalizer
{
    private const int RepeatedLineThreshold = 3;
    private const int MaxRepeatedLineLength = 120;

    /// <inheritdoc />
    public string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = NormalizeLineEndings(text);
        normalized = HyphenatedLineBreakRegex().Replace(normalized, string.Empty);
        normalized = NormalizeLegalMarkers(normalized);

        var lines = normalized
            .Split('\n')
            .Select(line => ExcessiveHorizontalWhitespaceRegex().Replace(line.Trim(), " "))
            .ToArray();
        var repeatedLines = FindRepeatedHeaderFooterLines(lines);
        var keptLines = new List<string>();
        var lastWasEmpty = false;

        foreach (var line in lines)
        {
            if (PageNumberOnlyLineRegex().IsMatch(line))
            {
                continue;
            }

            if (repeatedLines.Contains(NormalizeLineKey(line)))
            {
                continue;
            }

            if (line.Length == 0)
            {
                if (!lastWasEmpty)
                {
                    keptLines.Add(string.Empty);
                }

                lastWasEmpty = true;
                continue;
            }

            keptLines.Add(line);
            lastWasEmpty = false;
        }

        return string.Join(Environment.NewLine, keptLines).Trim();
    }

    private static string NormalizeLineEndings(string text) =>
        text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static string NormalizeLegalMarkers(string text)
    {
        var result = ArticleMarkerRegex().Replace(text, "ARTÍCULO");
        result = TitleMarkerRegex().Replace(result, "TÍTULO");
        result = ChapterMarkerRegex().Replace(result, "CAPÍTULO");
        return SectionMarkerRegex().Replace(result, "SECCIÓN");
    }

    private static HashSet<string> FindRepeatedHeaderFooterLines(IEnumerable<string> lines) =>
        lines
            .Select(NormalizeLineKey)
            .Where(line => line.Length is >= 8 and <= MaxRepeatedLineLength)
            .Where(line => !LegalMarkerLineRegex().IsMatch(line))
            .GroupBy(line => line, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= RepeatedLineThreshold)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeLineKey(string line) =>
        ExcessiveHorizontalWhitespaceRegex()
            .Replace(line.Trim(), " ")
            .ToUpperInvariant();

    [GeneratedRegex(@"(?<=\p{L})-\n(?=\p{Ll})", RegexOptions.Compiled)]
    private static partial Regex HyphenatedLineBreakRegex();

    [GeneratedRegex(@"[ \t]{2,}", RegexOptions.Compiled)]
    private static partial Regex ExcessiveHorizontalWhitespaceRegex();

    [GeneratedRegex(@"^\s*\d+\s*$", RegexOptions.Compiled)]
    private static partial Regex PageNumberOnlyLineRegex();

    [GeneratedRegex(@"\bART[IÍ]CULO\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ArticleMarkerRegex();

    [GeneratedRegex(@"\bT[IÍ]TULO\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TitleMarkerRegex();

    [GeneratedRegex(@"\bCAP[IÍ]TULO\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ChapterMarkerRegex();

    [GeneratedRegex(@"\bSECCI[OÓ]N\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SectionMarkerRegex();

    [GeneratedRegex(@"^(ARTÍCULO|TÍTULO|CAPÍTULO|SECCIÓN)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LegalMarkerLineRegex();
}
