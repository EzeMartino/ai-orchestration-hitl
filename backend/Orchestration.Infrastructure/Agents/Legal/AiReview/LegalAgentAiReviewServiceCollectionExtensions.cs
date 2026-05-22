using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orchestration.Application.Agents.Legal;
using Orchestration.Application.Agents.Legal.AiReview;
using Orchestration.Infrastructure.Agents.Planner.Reasoning;

namespace Orchestration.Infrastructure.Agents.Legal.AiReview;

public static class LegalAgentAiReviewServiceCollectionExtensions
{
    public static IServiceCollection AddLegalAgentAiReview(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<DeterministicLegalAnalysisReviewService>();
        services.AddScoped<SemanticKernelLegalAnalysisReviewResponseParser>();

        services.Configure<LegalAgentOptions>(
            configuration.GetSection(LegalAgentOptions.SectionName)
        );

        var legalAgentOptions = configuration
            .GetSection(LegalAgentOptions.SectionName)
            .Get<LegalAgentOptions>() ?? new LegalAgentOptions();

        var llmOptions = configuration
            .GetSection(LlmOptions.SectionName)
            .Get<LlmOptions>() ?? new LlmOptions();

        if (!legalAgentOptions.AiReviewEnabled || !llmOptions.Enabled)
        {
            services.AddScoped<ILegalAnalysisReviewService>(
                provider => provider.GetRequiredService<DeterministicLegalAnalysisReviewService>()
            );

            return services;
        }

        services.AddScoped<ILegalAnalysisReviewService, SemanticKernelLegalAnalysisReviewService>();

        return services;
    }
}
