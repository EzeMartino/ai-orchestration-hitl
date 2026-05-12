using CnvRegulation.Infrastructure.Search;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class StaticRegulationQueryExpanderTests
{
    [Fact]
    public void QueryExpander_ShouldExpandAlyc()
    {
        var expander = new StaticRegulationQueryExpander(new RegulationAliasesOptions());

        var expansion = expander.Expand("ALyC obligaciones");

        expansion.OriginalQuery.Should().Be("ALyC obligaciones");
        expansion.NormalizedQuery.Should().Be("alyc obligaciones");
        expansion.ExpandedTerms.Should().Contain("agente de liquidaci\u00f3n y compensaci\u00f3n");
        expansion.SearchQueries.Should().Contain("agente de liquidaci\u00f3n y compensaci\u00f3n obligaciones");
    }

    [Fact]
    public void QueryExpander_ShouldExpandFci()
    {
        var expander = new StaticRegulationQueryExpander(new RegulationAliasesOptions());

        var expansion = expander.Expand("FCI valuacion");

        expansion.ExpandedTerms.Should().Contain("fondo com\u00fan de inversi\u00f3n");
        expansion.SearchQueries.Should().Contain("fondos comunes de inversi\u00f3n valuacion");
    }

    [Fact]
    public void QueryExpander_ShouldNormalizeAccentsAndCase()
    {
        var expander = new StaticRegulationQueryExpander(new RegulationAliasesOptions());

        var expansion = expander.Expand("R\u00c9GIMEN INFORMATIVO");

        expansion.NormalizedQuery.Should().Be("regimen informativo");
        expansion.ExpandedTerms.Should().Contain("informaci\u00f3n peri\u00f3dica");
    }

    [Fact]
    public void QueryExpander_ShouldPreserveOriginalQuery()
    {
        var expander = new StaticRegulationQueryExpander(new RegulationAliasesOptions());

        var expansion = expander.Expand("  hecho relevante emisoras  ");

        expansion.OriginalQuery.Should().Be("hecho relevante emisoras");
    }

    [Fact]
    public void QueryExpander_ShouldLimitExpansionCount()
    {
        var expander = new StaticRegulationQueryExpander(new RegulationAliasesOptions
        {
            MaxSearchQueries = 3
        });

        var expansion = expander.Expand("lavado agentes");

        expansion.SearchQueries.Should().HaveCount(3);
        expansion.SearchQueries[0].Should().Be("lavado agentes");
    }

    [Theory]
    [InlineData("hecho-relevante", "hecho relevante")]
    [InlineData("fiduciario-financiero", "fiduciario financiero")]
    [InlineData("regimen_informativo", "regimen informativo")]
    [InlineData("rg-622", "rg 622")]
    public void QueryNormalizer_ShouldReplaceHyphensAndUnderscoresBetweenWords(string query, string expected)
    {
        StaticRegulationQueryExpander.Normalize(query).Should().Be(expected);
    }

    [Fact]
    public void QueryExpander_ShouldExpandHechoRelevante()
    {
        var expander = new StaticRegulationQueryExpander(new RegulationAliasesOptions());

        var expansion = expander.Expand("hecho relevante");

        expansion.SearchQueries.Should().Contain("informaci\u00f3n relevante");
        expansion.SearchQueries.Should().Contain("informaciones relevantes");
    }

    [Fact]
    public void QueryExpander_ShouldExpandInformacionRelevante()
    {
        var expander = new StaticRegulationQueryExpander(new RegulationAliasesOptions());

        var expansion = expander.Expand("informacion-relevante");

        expansion.NormalizedQuery.Should().Be("informacion relevante");
        expansion.SearchQueries.Should().Contain("hecho relevante");
        expansion.SearchQueries.Should().Contain("informaciones relevantes");
    }

    [Fact]
    public void QueryExpander_ShouldExpandFiduciarioFinanciero()
    {
        var expander = new StaticRegulationQueryExpander(new RegulationAliasesOptions());

        var expansion = expander.Expand("fiduciario-financiero");

        expansion.NormalizedQuery.Should().Be("fiduciario financiero");
        expansion.SearchQueries.Should().Contain("fideicomiso financiero");
        expansion.SearchQueries.Should().Contain("fideicomisos financieros");
    }

    [Fact]
    public void QueryExpander_ShouldExpandEmisora()
    {
        var expander = new StaticRegulationQueryExpander(new RegulationAliasesOptions());

        var expansion = expander.Expand("emisora");

        expansion.SearchQueries.Should().Contain("emisor");
        expansion.SearchQueries.Should().Contain("entidades emisoras");
        expansion.SearchQueries.Should().Contain("emisores");
    }
}
