using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Orchestration.Application.Agents.Data.FinancialAnalysis;

public static class FinancialMetricNameCatalog
{
    private static readonly Regex TokenRegex = new(
        @"[\p{L}\p{Nd}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyList<MetricAlias> Aliases =
        CreateAliases();

    private static readonly IReadOnlyDictionary<string, string>
        CanonicalNamesByAlias = Aliases
            .GroupBy(alias => Normalize(alias.Value), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().CanonicalName,
                StringComparer.OrdinalIgnoreCase);

    public static bool TryNormalize(
        string suppliedName,
        out string canonicalName)
    {
        return CanonicalNamesByAlias.TryGetValue(
            Normalize(suppliedName),
            out canonicalName!);
    }

    public static bool EvidenceSupports(
        string canonicalName,
        string evidence)
    {
        var evidenceTokens = Tokenize(evidence);
        var occurrences = FindOccurrences(evidenceTokens);
        var resolvedCanonicalNames = occurrences
            .Where(candidate =>
                !occurrences.Any(other =>
                    !string.Equals(
                        other.CanonicalName,
                        candidate.CanonicalName,
                        StringComparison.Ordinal) &&
                    other.TokenLength >= candidate.TokenLength &&
                    Overlaps(candidate, other)))
            .Select(candidate => candidate.CanonicalName)
            .ToHashSet(StringComparer.Ordinal);

        return resolvedCanonicalNames.Count == 1 &&
            resolvedCanonicalNames.Contains(canonicalName);
    }

    public static bool HasLeadingAlias(string value)
    {
        return TryMatchLeadingAlias(
            value,
            out _,
            out _);
    }

    public static bool TryMatchLeadingAlias(
        string value,
        out string canonicalName,
        out int matchedLength)
    {
        canonicalName = string.Empty;
        matchedLength = 0;
        var normalizedValue = Normalize(value.TrimStart());

        foreach (var alias in Aliases.OrderByDescending(
                     alias => alias.Value.Length))
        {
            var normalizedAlias = Normalize(alias.Value);

            if (!normalizedValue.StartsWith(
                    normalizedAlias,
                    StringComparison.OrdinalIgnoreCase) ||
                normalizedValue.Length > normalizedAlias.Length &&
                char.IsLetterOrDigit(
                    normalizedValue[normalizedAlias.Length]))
            {
                continue;
            }

            canonicalName = alias.CanonicalName;
            matchedLength = alias.Value.Length;
            return true;
        }

        return false;
    }

    private static IReadOnlyList<MetricAlias> CreateAliases()
    {
        var aliases = new List<MetricAlias>();

        AddAliases(aliases, "revenue", "Revenue", "Sales", "Ventas", "Ingresos", "Ventas netas");
        AddAliases(aliases, "gross_profit", "Gross Profit", "Ganancia bruta");
        AddAliases(aliases, "ebitda", "EBITDA");
        AddAliases(aliases, "ebit", "EBIT");
        AddAliases(aliases, "operating_income", "Operating Income", "Resultado operativo");
        AddAliases(aliases, "net_income", "Net Income");
        AddAliases(aliases, "cash", "Cash");
        AddAliases(aliases, "short_term_investments", "Short Term Investments");
        AddAliases(aliases, "receivables", "Receivables");
        AddAliases(aliases, "inventory", "Inventory");
        AddAliases(aliases, "current_assets", "Current Assets", "Activo corriente");
        AddAliases(aliases, "current_liabilities", "Current Liabilities", "Pasivo corriente");
        AddAliases(aliases, "total_debt", "Total Debt", "Deuda financiera total");
        AddAliases(aliases, "net_debt", "Net Debt", "Deuda neta");
        AddAliases(aliases, "equity", "Equity", "Patrimonio neto");
        AddAliases(aliases, "free_cash_flow", "Free Cash Flow", "FCF", "Flujo de caja libre");
        AddAliases(aliases, "capex", "Capex", "Capital Expenditures");
        AddAliases(aliases, "interest_expense", "Interest Expense");
        AddAliases(aliases, "shares", "Shares");
        AddAliases(aliases, "gross_margin", "Gross Margin", "Margen bruto");
        AddAliases(aliases, "operating_margin", "Operating Margin", "Margen operativo");
        AddAliases(aliases, "ebitda_margin", "EBITDA Margin", "Margen EBITDA");
        AddAliases(aliases, "net_margin", "Net Margin", "Margen neto");
        AddAliases(aliases, "current_ratio", "Current Ratio");
        AddAliases(aliases, "quick_ratio", "Quick Ratio");
        AddAliases(aliases, "debt_to_equity", "Debt to Equity");
        AddAliases(aliases, "net_debt_to_ebitda", "Net Debt to EBITDA");
        AddAliases(aliases, "interest_coverage", "Interest Coverage");
        AddAliases(aliases, "fcf_margin", "FCF Margin");
        AddAliases(aliases, "capex_to_revenue", "Capex to Revenue");

        return aliases;
    }

    private static void AddAliases(
        ICollection<MetricAlias> aliases,
        string canonicalName,
        params string[] supportedAliases)
    {
        aliases.Add(CreateAlias(canonicalName, canonicalName));

        foreach (var alias in supportedAliases)
        {
            aliases.Add(CreateAlias(alias, canonicalName));
        }
    }

    private static MetricAlias CreateAlias(
        string value,
        string canonicalName)
    {
        return new MetricAlias(
            value,
            canonicalName,
            Tokenize(value));
    }

    private static IReadOnlyList<string> Tokenize(string value)
    {
        return TokenRegex
            .Matches(value)
            .Select(match => Normalize(match.Value).ToLowerInvariant())
            .ToArray();
    }

    private static IReadOnlyList<AliasOccurrence> FindOccurrences(
        IReadOnlyList<string> evidenceTokens)
    {
        var occurrences = new List<AliasOccurrence>();

        foreach (var alias in Aliases)
        {
            for (var start = 0;
                 start <= evidenceTokens.Count - alias.Tokens.Count;
                 start++)
            {
                if (MatchesAt(evidenceTokens, alias.Tokens, start))
                {
                    occurrences.Add(new AliasOccurrence(
                        start,
                        alias.Tokens.Count,
                        alias.CanonicalName));
                }
            }
        }

        return occurrences;
    }

    private static bool MatchesAt(
        IReadOnlyList<string> evidenceTokens,
        IReadOnlyList<string> aliasTokens,
        int start)
    {
        for (var index = 0; index < aliasTokens.Count; index++)
        {
            if (!string.Equals(
                    evidenceTokens[start + index],
                    aliasTokens[index],
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Overlaps(
        AliasOccurrence first,
        AliasOccurrence second)
    {
        return first.Start < second.Start + second.TokenLength &&
            second.Start < first.Start + first.TokenLength;
    }

    private static string Normalize(string value)
    {
        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) !=
                UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private sealed record MetricAlias(
        string Value,
        string CanonicalName,
        IReadOnlyList<string> Tokens);

    private sealed record AliasOccurrence(
        int Start,
        int TokenLength,
        string CanonicalName);
}
