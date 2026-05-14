using FluentAssertions;
using Orchestration.Application.Agents.Planner.Reasoning;
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
            "Semantic Kernel + OpenAI/test-model",
            "OpenAI",
            "test-model"
        );

        result.Should().NotBeNull();
        result!.Engine.Should().Be("Semantic Kernel + OpenAI/test-model");
        result.UsedLlm.Should().BeTrue();
        result.UsedFallback.Should().BeFalse();
        result.Provider.Should().Be("OpenAI");
        result.Model.Should().Be("test-model");
        result.FailureReason.Should().BeNull();
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
            "Semantic Kernel + OpenAI/test-model",
            "OpenAI",
            "test-model"
        );

        result.Should().BeNull();
    }

    [Fact]
    public void ApplyFallbackMetadata_Should_include_safe_failure_reason()
    {
        var fallback = new PlannerReasoningResult(
            Engine: "Deterministic Planner Reasoning",
            Summary: "Fallback summary.",
            RecommendedActions: ["Review evidence."],
            RiskFactors: ["Risk factor."],
            Limitations: ["No LLM reasoning was used."],
            UsedLlm: false,
            UsedFallback: true,
            Provider: null,
            Model: null,
            FailureReason: null
        );

        var result = SemanticKernelPlannerReasoningService.ApplyFallbackMetadata(
            fallback,
            new LlmOptions
            {
                Enabled = true,
                Provider = "OpenAI",
                Model = "test-model",
                ApiKey = "not-used"
            },
            SemanticKernelPlannerReasoningService.SanitizeFailure(new System.Text.Json.JsonException())
        );

        result.UsedLlm.Should().BeFalse();
        result.UsedFallback.Should().BeTrue();
        result.Provider.Should().Be("OpenAI");
        result.Model.Should().Be("test-model");
        result.FailureReason.Should().Be("LLM returned invalid JSON.");
        result.FailureReason.Should().NotContain("System.Text.Json");
    }
}
