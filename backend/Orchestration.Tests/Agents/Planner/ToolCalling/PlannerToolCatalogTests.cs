using FluentAssertions;
using Orchestration.Application.Agents.Planner.ToolCalling;

namespace Orchestration.Tests.Agents.Planner.ToolCalling;

public sealed class PlannerToolCatalogTests
{
    [Fact]
    public void All_Should_define_only_composable_tools_with_unique_names()
    {
        PlannerToolCatalog.All.Should().HaveCount(2);
        PlannerToolCatalog.All
            .Select(tool => tool.Name)
            .Should()
            .OnlyHaveUniqueItems();
        PlannerToolCatalog.All.Should().OnlyContain(tool =>
            !string.IsNullOrWhiteSpace(tool.PromptDescription) &&
            !string.IsNullOrWhiteSpace(tool.AuditActor));
    }

    [Fact]
    public void Find_Should_define_legal_review_as_argument_free_composite_capability()
    {
        var definition = PlannerToolCatalog.Find(
            PlannerToolCatalog.SearchCnvRegulationName);

        definition.Should().NotBeNull();
        definition!.Arguments.Should().BeEmpty();
        definition.PromptDescription.Should().Be(
            "Autoriza una revisión legal compuesta; la lógica determinista deriva consultas CNV del análisis financiero completado.");
    }

    [Theory]
    [InlineData(
        "data.analyze_transactions",
        PlannerToolHandler.AnalyzeTransactions,
        PlannerToolResultKind.DataAgent)]
    [InlineData(
        "legal.search_cnv_regulation",
        PlannerToolHandler.SearchCnvRegulation,
        PlannerToolResultKind.LegalAgent)]
    public void Find_Should_return_canonical_definition(
        string name,
        PlannerToolHandler handler,
        PlannerToolResultKind resultKind)
    {
        var definition = PlannerToolCatalog.Find(name);

        definition.Should().NotBeNull();
        definition!.Handler.Should().Be(handler);
        definition.ResultKind.Should().Be(resultKind);
    }

    [Fact]
    public void GetAllowed_Should_intersect_catalog_with_configured_allowlist()
    {
        var options = new ToolCallingOptions
        {
            AllowedTools = ["legal.search_cnv_regulation", "unknown.tool"]
        };

        var allowed = PlannerToolCatalog.GetAllowed(options);

        allowed.Should().ContainSingle()
            .Which.Name.Should().Be("legal.search_cnv_regulation");
    }

    [Fact]
    public void Find_Should_match_names_case_insensitively()
    {
        var definition = PlannerToolCatalog.Find("DATA.ANALYZE_TRANSACTIONS");

        definition.Should().NotBeNull();
        definition!.Handler.Should().Be(PlannerToolHandler.AnalyzeTransactions);
    }
}
