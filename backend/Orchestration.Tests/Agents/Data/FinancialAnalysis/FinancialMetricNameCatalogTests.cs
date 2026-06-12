using FluentAssertions;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class FinancialMetricNameCatalogTests
{
    [Theory]
    [InlineData("ebitda", "EBITDA Margin 2024A 12.5%")]
    [InlineData("cash", "Free Cash Flow 2024A 100")]
    [InlineData("capex", "Capex to Revenue 2024A 0.2")]
    [InlineData("revenue", "Capex to Revenue 2024A 0.2")]
    public void EvidenceSupports_ShorterAliasOverlapsLongerDifferentMetric_ReturnsFalse(
        string canonicalName,
        string evidence)
    {
        var result = FinancialMetricNameCatalog.EvidenceSupports(
            canonicalName,
            evidence);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("ebitda_margin", "EBITDA Margin 2024A 12.5%")]
    [InlineData("free_cash_flow", "Free Cash Flow 2024A 100")]
    [InlineData("capex_to_revenue", "Capex to Revenue 2024A 0.2")]
    [InlineData("ebitda", "EBITDA 2024A 100")]
    public void EvidenceSupports_LongestAliasOrStandaloneAliasMatches_ReturnsTrue(
        string canonicalName,
        string evidence)
    {
        var result = FinancialMetricNameCatalog.EvidenceSupports(
            canonicalName,
            evidence);

        result.Should().BeTrue();
    }
}
