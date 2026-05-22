using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;
using Orchestration.Infrastructure.Agents.Planner.Reasoning;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.AiReview;

public static class DataAgentAiReviewServiceCollectionExtensions
{
    public static IServiceCollection AddDataAgentAiReview(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<DeterministicDataAgentAiReviewService>();
        services.AddScoped<SemanticKernelDataAgentAiReviewResponseParser>();

        var dataAgentOptions = configuration
            .GetSection(DataAgentOptions.SectionName)
            .Get<DataAgentOptions>() ?? new DataAgentOptions();
        var llmOptions = configuration
            .GetSection(LlmOptions.SectionName)
            .Get<LlmOptions>() ?? new LlmOptions();

        if (!dataAgentOptions.AiReviewEnabled || !llmOptions.Enabled)
        {
            services.AddScoped<IDataAgentAiReviewService>(
                provider => provider.GetRequiredService<DeterministicDataAgentAiReviewService>()
            );

            return services;
        }

        ValidateOptions(llmOptions);

        services.AddScoped<IDataAgentAiReviewService, SemanticKernelDataAgentAiReviewService>();

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
                $"DataAgent:AiReviewEnabled and Llm:Enabled are true but required configuration is missing: {string.Join(", ", missingRequiredValues)}."
            );
        }

        if (string.IsNullOrWhiteSpace(options.ServiceId))
        {
            throw new InvalidOperationException(
                "Llm:ServiceId is required when DataAgent:AiReviewEnabled and Llm:Enabled are true."
            );
        }
    }
}
