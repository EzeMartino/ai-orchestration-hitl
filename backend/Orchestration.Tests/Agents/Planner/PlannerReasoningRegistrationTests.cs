using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orchestration.Application.Agents.Planner.Reasoning;
using Orchestration.Infrastructure.Agents.Planner.Reasoning;

namespace Orchestration.Tests.Agents.Planner;

public class PlannerReasoningRegistrationTests
{
    [Fact]
    public void AddPlannerReasoning_Should_not_require_api_key_when_llm_is_disabled()
    {
        var configuration = new ConfigurationManager
        {
            ["Llm:Enabled"] = "false",
            ["Llm:Provider"] = "OpenAI",
            ["Llm:Model"] = "",
            ["Llm:ApiKey"] = ""
        };

        var services = new ServiceCollection();

        services.AddPlannerReasoning(configuration);

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IPlannerReasoningService>();

        service.Should().BeOfType<DeterministicPlannerReasoningService>();
    }

    [Fact]
    public void AddPlannerReasoning_Should_validate_model_and_api_key_when_llm_is_enabled()
    {
        var configuration = new ConfigurationManager
        {
            ["Llm:Enabled"] = "true",
            ["Llm:Provider"] = "OpenAI",
            ["Llm:Model"] = "",
            ["Llm:ApiKey"] = ""
        };

        var services = new ServiceCollection();

        var act = () => services.AddPlannerReasoning(configuration);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Llm:Enabled is true but required configuration is missing: Llm:Model, Llm:ApiKey.");
    }

    [Fact]
    public void AddPlannerReasoning_Should_validate_api_key_when_llm_is_enabled()
    {
        var configuration = new ConfigurationManager
        {
            ["Llm:Enabled"] = "true",
            ["Llm:Provider"] = "OpenAI",
            ["Llm:Model"] = "test-model",
            ["Llm:ApiKey"] = ""
        };

        var services = new ServiceCollection();

        var act = () => services.AddPlannerReasoning(configuration);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Llm:Enabled is true but required configuration is missing: Llm:ApiKey.");
    }
}
