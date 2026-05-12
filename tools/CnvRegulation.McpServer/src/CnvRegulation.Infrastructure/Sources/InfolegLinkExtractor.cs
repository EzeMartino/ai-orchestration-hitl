using System.Net;
using System.Text.RegularExpressions;

namespace CnvRegulation.Infrastructure.Sources;

/// <summary>
/// Extracts and validates official Infoleg links from HTML.
/// </summary>
public sealed partial class InfolegLinkExtractor
{
    private const string AllowedHost = "servicios.infoleg.gob.ar";
    private const string AllowedPathPrefix = "/infolegInternet/";

    /// <summary>
    /// Extracts official Infoleg link candidates from HTML.
    /// </summary>
    /// <param name="html">The HTML content.</param>
    /// <param name="baseUri">The source document URI.</param>
    /// <returns>Accepted link candidates and discovered/skipped counts.</returns>
    public InfolegLinkExtractionResult Extract(string html, Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);

        if (string.IsNullOrWhiteSpace(html))
        {
            return new InfolegLinkExtractionResult
            {
                Candidates = [],
                LinksDiscovered = 0,
                LinksSkipped = 0
            };
        }

        var candidates = new Dictionary<string, InfolegLinkCandidate>(StringComparer.OrdinalIgnoreCase);
        var discovered = 0;
        var skipped = 0;

        foreach (Match match in HrefRegex().Matches(html))
        {
            discovered++;
            var rawHref = WebUtility.HtmlDecode(match.Groups["href"].Value).Trim();
            if (!TryNormalizeInfolegUrl(rawHref, baseUri, out var normalizedUrl))
            {
                skipped++;
                continue;
            }

            if (!candidates.TryAdd(
                    normalizedUrl,
                    new InfolegLinkCandidate
                    {
                        Url = normalizedUrl,
                        Classification = Classify(normalizedUrl)
                    }))
                {
                    skipped++;
                }
        }

        return new InfolegLinkExtractionResult
        {
            Candidates = candidates.Values.ToArray(),
            LinksDiscovered = discovered,
            LinksSkipped = skipped
        };
    }

    /// <summary>
    /// Classifies an Infoleg URL.
    /// </summary>
    /// <param name="url">The canonical URL.</param>
    /// <returns>The classification.</returns>
    public static InfolegLinkClassification Classify(string url)
    {
        if (url.Contains("/norma.htm", StringComparison.OrdinalIgnoreCase))
        {
            return InfolegLinkClassification.Norma;
        }

        if (url.Contains("/texact.htm", StringComparison.OrdinalIgnoreCase))
        {
            return InfolegLinkClassification.Texact;
        }

        if (url.Contains("/anexos/", StringComparison.OrdinalIgnoreCase))
        {
            return InfolegLinkClassification.Anexos;
        }

        if (url.Contains("verNorma.do", StringComparison.OrdinalIgnoreCase))
        {
            return InfolegLinkClassification.VerNorma;
        }

        return InfolegLinkClassification.Unknown;
    }

    private static bool TryNormalizeInfolegUrl(string href, Uri baseUri, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;

        if (string.IsNullOrWhiteSpace(href)
            || href.StartsWith('#')
            || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Uri.TryCreate(baseUri, href, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.Equals(uri.Host, AllowedHost, StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith(AllowedPathPrefix, StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.Contains("..", StringComparison.Ordinal)
            || uri.AbsolutePath.Contains("%2e", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsAllowedQuery(uri))
        {
            return false;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = -1,
            Fragment = string.Empty
        };

        normalizedUrl = builder.Uri.AbsoluteUri;
        return true;
    }

    private static bool IsAllowedQuery(Uri uri)
    {
        if (string.IsNullOrWhiteSpace(uri.Query))
        {
            return true;
        }

        if (!uri.AbsolutePath.EndsWith("verNorma.do", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = uri.Query.TrimStart('?');
        return query
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2)[0])
            .All(key =>
                key.Equals("id", StringComparison.OrdinalIgnoreCase)
                || key.Equals("nro", StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex("""href\s*=\s*["'](?<href>[^"']+)["']""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HrefRegex();
}
