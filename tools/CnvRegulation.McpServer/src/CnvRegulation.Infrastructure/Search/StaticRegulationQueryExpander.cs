using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Search;

namespace CnvRegulation.Infrastructure.Search;

/// <summary>
/// Expands CNV regulatory query aliases from a bounded static dictionary.
/// </summary>
public sealed partial class StaticRegulationQueryExpander(RegulationAliasesOptions options) : IRegulationQueryExpander
{
    private readonly IReadOnlyList<RegulationQueryTerm> terms = options.Aliases
        .Select(alias => new RegulationQueryTerm
        {
            Alias = alias.Key,
            NormalizedAlias = Normalize(alias.Key),
            ExpandedTerms = alias.Value
        })
        .OrderByDescending(term => term.NormalizedAlias.Length)
        .ToArray();

    /// <inheritdoc />
    public RegulationQueryExpansion Expand(string query)
    {
        var originalQuery = string.IsNullOrWhiteSpace(query) ? string.Empty : query.Trim();
        var normalizedQuery = Normalize(originalQuery);
        if (normalizedQuery.Length == 0)
        {
            return new RegulationQueryExpansion
            {
                OriginalQuery = originalQuery,
                NormalizedQuery = normalizedQuery,
                ExpandedTerms = [],
                SearchQueries = []
            };
        }

        var searchQueries = new List<string> { originalQuery };
        var expandedTerms = new List<string>();

        foreach (var term in terms)
        {
            if (!ContainsAlias(normalizedQuery, term.NormalizedAlias))
            {
                continue;
            }

            var remainder = RemoveAlias(normalizedQuery, term.NormalizedAlias);
            foreach (var expandedTerm in term.ExpandedTerms)
            {
                expandedTerms.Add(expandedTerm);
                searchQueries.Add(JoinQuery(expandedTerm, remainder));
            }

            if (searchQueries.Count >= options.MaxSearchQueries)
            {
                break;
            }
        }

        return new RegulationQueryExpansion
        {
            OriginalQuery = originalQuery,
            NormalizedQuery = normalizedQuery,
            ExpandedTerms = Distinct(expandedTerms).ToArray(),
            SearchQueries = Distinct(searchQueries)
                .Take(Math.Max(1, options.MaxSearchQueries))
                .ToArray()
        };
    }

    /// <summary>
    /// Normalizes query text for accent-insensitive alias matching.
    /// </summary>
    /// <param name="value">The text to normalize.</param>
    /// <returns>The normalized text.</returns>
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var withWordSeparators = WordSeparatorRegex().Replace(value.Trim(), " ");
        var normalized = withWordSeparators.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return ExcessiveWhitespaceRegex().Replace(builder.ToString().Normalize(NormalizationForm.FormC), " ");
    }

    private static bool ContainsAlias(string query, string alias) =>
        Regex.IsMatch(query, $@"(^|\s){Regex.Escape(alias)}($|\s)", RegexOptions.IgnoreCase);

    private static string RemoveAlias(string query, string alias) =>
        ExcessiveWhitespaceRegex()
            .Replace(Regex.Replace(query, $@"(^|\s){Regex.Escape(alias)}($|\s)", " ", RegexOptions.IgnoreCase), " ")
            .Trim();

    private static string JoinQuery(string expandedTerm, string remainder) =>
        string.IsNullOrWhiteSpace(remainder)
            ? expandedTerm
            : $"{expandedTerm} {remainder}";

    private static IEnumerable<string> Distinct(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex ExcessiveWhitespaceRegex();

    [GeneratedRegex(@"(?<=[\p{L}\p{N}])[-_](?=[\p{L}\p{N}])|_", RegexOptions.Compiled)]
    private static partial Regex WordSeparatorRegex();
}
