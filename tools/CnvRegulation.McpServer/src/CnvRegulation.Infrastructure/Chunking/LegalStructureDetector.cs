using System.Text.RegularExpressions;

namespace CnvRegulation.Infrastructure.Chunking;

/// <summary>
/// Detects simple legal structure markers in CNV-like text.
/// </summary>
public sealed class LegalStructureDetector
{
    private static readonly Regex TitleRegex = new(
        @"^\s*T[\u00cdI]TULO\s+(?<value>[IVXLCDM]+|\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ChapterRegex = new(
        @"^\s*CAP[\u00cdI]TULO\s+(?<value>[IVXLCDM]+|\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SectionRegex = new(
        @"^\s*SECCI[\u00d3O]N\s+(?<value>[IVXLCDM]+|\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ArticleRegex = new(
        @"^\s*ART[\u00cdI]CULO\s+(?<number>\d+[\u00b0\u00ba]?)\s*[\.\-\u2013\u2014:]?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ArticleNumberRegex = new(
        @"(?:ART[\u00cdI]CULO)?\s*(?<number>\d+[\u00b0\u00ba]?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Attempts to detect an article marker from one text line.
    /// </summary>
    /// <param name="line">The source line.</param>
    /// <returns>The normalized article label when found; otherwise, null.</returns>
    public string? DetectArticle(string line)
    {
        var match = ArticleRegex.Match(line);
        if (!match.Success)
        {
            return null;
        }

        return $"Art\u00edculo {NormalizeNumber(match.Groups["number"].Value)}";
    }

    /// <summary>
    /// Attempts to detect a title marker from one text line.
    /// </summary>
    /// <param name="line">The source line.</param>
    /// <returns>The normalized title label when found; otherwise, null.</returns>
    public string? DetectTitle(string line)
    {
        var match = TitleRegex.Match(line);
        if (!match.Success)
        {
            return null;
        }

        return $"T\u00edtulo {match.Groups["value"].Value.Trim()}";
    }

    /// <summary>
    /// Attempts to detect a chapter marker from one text line.
    /// </summary>
    /// <param name="line">The source line.</param>
    /// <returns>The normalized chapter label when found; otherwise, null.</returns>
    public string? DetectChapter(string line)
    {
        var match = ChapterRegex.Match(line);
        if (!match.Success)
        {
            return null;
        }

        return $"Cap\u00edtulo {match.Groups["value"].Value.Trim()}";
    }

    /// <summary>
    /// Attempts to detect a section marker from one text line.
    /// </summary>
    /// <param name="line">The source line.</param>
    /// <returns>The normalized section label when found; otherwise, null.</returns>
    public string? DetectSection(string line)
    {
        var match = SectionRegex.Match(line);
        if (!match.Success)
        {
            return null;
        }

        return $"Secci\u00f3n {match.Groups["value"].Value.Trim()}";
    }

    /// <summary>
    /// Normalizes article labels for comparison.
    /// </summary>
    /// <param name="article">The article label to normalize.</param>
    /// <returns>A comparable article key.</returns>
    public static string NormalizeArticleKey(string article)
    {
        if (string.IsNullOrWhiteSpace(article))
        {
            return string.Empty;
        }

        var match = ArticleNumberRegex.Match(article);
        return match.Success ? NormalizeNumber(match.Groups["number"].Value) : article.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Creates a chunk identifier suffix for an article label.
    /// </summary>
    /// <param name="article">The article label.</param>
    /// <returns>The chunk identifier suffix.</returns>
    public static string CreateArticleSlug(string article)
    {
        var articleKey = NormalizeArticleKey(article);
        return string.IsNullOrWhiteSpace(articleKey)
            ? "chunk"
            : $"articulo-{articleKey.ToLowerInvariant()}";
    }

    private static string NormalizeNumber(string value) =>
        value.Trim().TrimEnd('\u00b0', '\u00ba').ToUpperInvariant();
}
