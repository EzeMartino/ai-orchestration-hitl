using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.AiReview;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis.AiReview;

public sealed class DataAgentAiReviewRegistrationTests
{
    [Fact]
    public void AddDataAgentAiReview_Should_use_deterministic_service_when_ai_review_is_disabled()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationManager
        {
            ["DataAgent:AiReviewEnabled"] = "false",
            ["Llm:Enabled"] = "true",
            ["Llm:Provider"] = "OpenAI",
            ["Llm:Model"] = "test-model",
            ["Llm:ApiKey"] = "not-used"
        };

        services.AddDataAgentAiReview(configuration);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IDataAgentAiReviewService>()
            .Should()
            .BeOfType<DeterministicDataAgentAiReviewService>();
    }

    [Fact]
    public void AddDataAgentAiReview_Should_use_deterministic_service_when_llm_is_disabled()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationManager
        {
            ["DataAgent:AiReviewEnabled"] = "true",
            ["Llm:Enabled"] = "false",
            ["Llm:Provider"] = "OpenAI",
            ["Llm:Model"] = "",
            ["Llm:ApiKey"] = ""
        };

        services.AddDataAgentAiReview(configuration);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IDataAgentAiReviewService>()
            .Should()
            .BeOfType<DeterministicDataAgentAiReviewService>();
    }

    [Fact]
    public void AddDataAgentAiReview_Should_validate_model_and_api_key_when_ai_review_and_llm_are_enabled()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationManager
        {
            ["DataAgent:AiReviewEnabled"] = "true",
            ["Llm:Enabled"] = "true",
            ["Llm:Provider"] = "OpenAI",
            ["Llm:Model"] = "",
            ["Llm:ApiKey"] = ""
        };

        var act = () => services.AddDataAgentAiReview(configuration);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "DataAgent:AiReviewEnabled and Llm:Enabled are true but required configuration is missing: Llm:Model, Llm:ApiKey."
            );
    }
}
