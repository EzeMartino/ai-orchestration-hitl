using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Infrastructure.Agents.Planner.Reasoning;

namespace Orchestration.Infrastructure.Agents.Planner.ToolCalling;

public static class ToolPlanProposalServiceCollectionExtensions
{
    public static IServiceCollection AddToolPlanProposal(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ToolCallingOptions>(
            configuration.GetSection(ToolCallingOptions.SectionName)
        );
        services.Configure<LlmOptions>(
            configuration.GetSection(LlmOptions.SectionName)
        );

        services.AddScoped<DeterministicToolPlanProposalService>(provider =>
            new DeterministicToolPlanProposalService(
                provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ToolCallingOptions>>().Value
            )
        );
        services.AddScoped<SemanticKernelToolPlanResponseParser>();

        var toolCallingOptions = configuration
            .GetSection(ToolCallingOptions.SectionName)
            .Get<ToolCallingOptions>() ?? new ToolCallingOptions();
        var llmOptions = configuration
            .GetSection(LlmOptions.SectionName)
            .Get<LlmOptions>() ?? new LlmOptions();

        if (toolCallingOptions.Enabled && llmOptions.Enabled)
        {
            services.AddScoped<IToolPlanProposalService, SemanticKernelToolPlanProposalService>();
        }
        else
        {
            services.AddScoped<IToolPlanProposalService>(provider =>
                provider.GetRequiredService<DeterministicToolPlanProposalService>()
            );
        }

        return services;
    }
}
