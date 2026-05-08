using System.Text.RegularExpressions;

namespace CnvRegulation.Infrastructure.Chunking;

/// <summary>
/// Detects simple legal structure markers in CNV-like text.
/// </summary>
public sealed partial class LegalStructureDetector
{
    /// <summary>
    /// Attempts to detect an article marker from one text line.
    /// </summary>
    /// <param name="line">The source line.</param>
    /// <returns>The normalized article label when found; otherwise, null.</returns>
    public string? DetectArticle(string line)
    {
        var match = ArticleRegex().Match(line);
        if (!match.Success)
        {
            return null;
        }

        return $"Artículo {NormalizeNumber(match.Groups["number"].Value)}";
    }

    /// <summary>
    /// Attempts to detect a title marker from one text line.
    /// </summary>
    /// <param name="line">The source line.</param>
    /// <returns>The normalized title label when found; otherwise, null.</returns>
    public string? DetectTitle(string line)
    {
        var match = TitleRegex().Match(line);
        if (!match.Success)
        {
            return null;
        }

        return $"Título {match.Groups["value"].Value.Trim()}";
    }

    /// <summary>
    /// Attempts to detect a chapter marker from one text line.
    /// </summary>
    /// <param name="line">The source line.</param>
    /// <returns>The normalized chapter label when found; otherwise, null.</returns>
    public string? DetectChapter(string line)
    {
        var match = ChapterRegex().Match(line);
        if (!match.Success)
        {
            return null;
        }

        return $"Capítulo {match.Groups["value"].Value.Trim()}";
    }

    /// <summary>
    /// Attempts to detect a section marker from one text line.
    /// </summary>
    /// <param name="line">The source line.</param>
    /// <returns>The normalized section label when found; otherwise, null.</returns>
    public string? DetectSection(string line)
    {
        var match = SectionRegex().Match(line);
        if (!match.Success)
        {
            return null;
        }

        return $"Sección {match.Groups["value"].Value.Trim()}";
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

        var match = ArticleNumberRegex().Match(article);
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
        value.Trim().TrimEnd('°', 'º').ToUpperInvariant();

    [GeneratedRegex(@"^\s*(ART[IÍ]CULO|Artículo|Articulo)\s+(?<number>\d+[°º]?)\s*[\.\-–—:]?", RegexOptions.IgnoreCase)]
    private static partial Regex ArticleRegex();

    [GeneratedRegex(@"^\s*T[ÍI]TULO\s+(?<value>[IVXLCDM]+|\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"^\s*CAP[ÍI]TULO\s+(?<value>[IVXLCDM]+|\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterRegex();

    [GeneratedRegex(@"^\s*SECCI[ÓO]N\s+(?<value>[IVXLCDM]+|\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SectionRegex();

    [GeneratedRegex(@"(?:ART[IÍ]CULO|Artículo|Articulo)?\s*(?<number>\d+[°º]?)", RegexOptions.IgnoreCase)]
    private static partial Regex ArticleNumberRegex();
}
