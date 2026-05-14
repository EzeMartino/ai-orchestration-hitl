using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orchestration.Application.Agents.Planner.Reasoning;

namespace Orchestration.Infrastructure.Agents.Planner.Reasoning;

public static class PlannerReasoningServiceCollectionExtensions
{
    public static IServiceCollection AddPlannerReasoning(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LlmOptions>(
            configuration.GetSection(LlmOptions.SectionName)
        );

        services.AddScoped<DeterministicPlannerReasoningService>();

        var options = configuration
            .GetSection(LlmOptions.SectionName)
            .Get<LlmOptions>() ?? new LlmOptions();

        if (!options.Enabled)
        {
            services.AddScoped<IPlannerReasoningService>(
                provider => provider.GetRequiredService<DeterministicPlannerReasoningService>()
            );

            return services;
        }

        ValidateOptions(options);

        services.AddScoped<IPlannerReasoningService, SemanticKernelPlannerReasoningService>();

        return services;
    }

    private static void ValidateOptions(LlmOptions options)
    {
        if (!string.Equals(options.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Llm:Provider '{options.Provider}' is not supported. Supported provider: OpenAI."
            );
        }

        var missingRequiredValues = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            missingRequiredValues.Add("Llm:Model");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            missingRequiredValues.Add("Llm:ApiKey");
        }

        if (missingRequiredValues.Count > 0)
        {
            throw new InvalidOperationException(
                $"Llm:Enabled is true but required configuration is missing: {string.Join(", ", missingRequiredValues)}."
            );
        }

        if (string.IsNullOrWhiteSpace(options.ServiceId))
        {
            throw new InvalidOperationException(
                "Llm:ServiceId is required when Llm:Enabled is true."
            );
        }
    }
}
