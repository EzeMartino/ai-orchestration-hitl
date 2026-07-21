using FluentAssertions;
using Orchestration.Application.Agents.Planner.ToolCalling;

namespace Orchestration.Tests.Agents.Planner.ToolCalling;

public sealed class PlannerStageSafeResultsTests
{
    [Fact]
    public void DataUnavailable_ReturnsUnknownHumanReviewResultWithoutFabricatedEvidence()
    {
        var result = PlannerStageSafeResults.DataUnavailable();

        result.HasAnomaly.Should().BeFalse();
        result.Severity.Should().Be("Unknown");
        result.Summary.Should().Be("El análisis de datos no produjo un resultado utilizable.");
        result.Engine.Should().Be("Controlled Tool Executor");
        result.Evidence.Should().BeEmpty();
        result.FinancialAnalysis.Should().BeNull();
        result.RequiresHumanReview.Should().BeTrue();
    }

    [Fact]
    public void LegalUnavailable_ReturnsUnknownHumanReviewResultWithoutFabricatedEvidence()
    {
        var result = PlannerStageSafeResults.LegalUnavailable();

        result.HasComplianceRisk.Should().BeFalse();
        result.RiskLevel.Should().Be("Unknown");
        result.Summary.Should().Be("La revisión legal no produjo un resultado utilizable.");
        result.Engine.Should().Be("Controlled Tool Executor");
        result.Evidence.Should().BeEmpty();
        result.Warnings.Should().Equal(
            "No hubo evidencia legal automatizada utilizable; se requiere revisión legal humana."
        );
        result.RequiresHumanReview.Should().BeTrue();
    }
}
