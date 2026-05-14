using FluentAssertions;
using Orchestration.Infrastructure.Agents.Planner.Reasoning;

namespace Orchestration.Tests.Agents.Planner;

public class SemanticKernelPlannerReasoningServiceTests
{
    [Fact]
    public void TryParseResponse_Should_parse_valid_json()
    {
        const string content = """
{
  "summary": "Human review is required.",
  "recommendedActions": ["Review anomaly evidence."],
  "riskFactors": ["High data severity."],
  "limitations": ["Not legal advice."]
}
""";

        var result = SemanticKernelPlannerReasoningService.TryParseResponse(
            content,
            "Semantic Kernel + OpenAI/test-model"
        );

        result.Should().NotBeNull();
        result!.Engine.Should().Be("Semantic Kernel + OpenAI/test-model");
        result.Summary.Should().Be("Human review is required.");
        result.RecommendedActions.Should().Contain("Review anomaly evidence.");
        result.RiskFactors.Should().Contain("High data severity.");
        result.Limitations.Should().Contain("Not legal advice.");
    }

    [Fact]
    public void TryParseResponse_Should_return_null_for_invalid_json()
    {
        var result = SemanticKernelPlannerReasoningService.TryParseResponse(
            "not-json",
            "Semantic Kernel + OpenAI/test-model"
        );

        result.Should().BeNull();
    }
}
