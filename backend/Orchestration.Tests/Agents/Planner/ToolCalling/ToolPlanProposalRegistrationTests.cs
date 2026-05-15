using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Infrastructure.Agents.Planner.ToolCalling;

namespace Orchestration.Tests.Agents.Planner.ToolCalling;

public class ToolPlanProposalRegistrationTests
{
    [Fact]
    public void AddToolPlanProposal_Should_use_deterministic_service_when_tool_calling_is_disabled()
    {
        var services = new ServiceCollection();

        services.AddToolPlanProposal(CreateConfiguration(
            toolCallingEnabled: false,
            llmEnabled: true
        ));

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IToolPlanProposalService>();

        service.Should().BeOfType<DeterministicToolPlanProposalService>();
    }

    [Fact]
    public void AddToolPlanProposal_Should_use_deterministic_service_when_llm_is_disabled()
    {
        var services = new ServiceCollection();

        services.AddToolPlanProposal(CreateConfiguration(
            toolCallingEnabled: true,
            llmEnabled: false
        ));

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IToolPlanProposalService>();

        service.Should().BeOfType<DeterministicToolPlanProposalService>();
    }

    [Fact]
    public void AddToolPlanProposal_Should_use_semantic_kernel_service_when_tool_calling_and_llm_are_enabled()
    {
        var services = new ServiceCollection();

        services.AddToolPlanProposal(CreateConfiguration(
            toolCallingEnabled: true,
            llmEnabled: true
        ));

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IToolPlanProposalService>();

        service.Should().BeOfType<SemanticKernelToolPlanProposalService>();
    }

    private static IConfiguration CreateConfiguration(
        bool toolCallingEnabled,
        bool llmEnabled)
    {
        return new ConfigurationManager
        {
            ["ToolCalling:Enabled"] = toolCallingEnabled.ToString(),
            ["Llm:Enabled"] = llmEnabled.ToString(),
            ["Llm:Provider"] = "OpenAI",
            ["Llm:Model"] = "test-model",
            ["Llm:ApiKey"] = "test-api-key",
            ["Llm:ServiceId"] = "planner-reasoning"
        };
    }
}
