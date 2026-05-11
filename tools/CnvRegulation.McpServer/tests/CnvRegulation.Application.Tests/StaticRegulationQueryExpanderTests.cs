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
        expansion.ExpandedTerms.Should().Contain("agente de liquidación y compensación");
        expansion.SearchQueries.Should().Contain("agente de liquidación y compensación obligaciones");
    }

    [Fact]
    public void QueryExpander_ShouldExpandFci()
    {
        var expander = new StaticRegulationQueryExpander(new RegulationAliasesOptions());

        var expansion = expander.Expand("FCI valuacion");

        expansion.ExpandedTerms.Should().Contain("fondo común de inversión");
        expansion.SearchQueries.Should().Contain("fondos comunes de inversión valuacion");
    }

    [Fact]
    public void QueryExpander_ShouldNormalizeAccentsAndCase()
    {
        var expander = new StaticRegulationQueryExpander(new RegulationAliasesOptions());

        var expansion = expander.Expand("RÉGIMEN INFORMATIVO");

        expansion.NormalizedQuery.Should().Be("regimen informativo");
        expansion.ExpandedTerms.Should().Contain("información periódica");
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
}
