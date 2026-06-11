using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;
using Orchestration.Infrastructure.Agents.Planner.Reasoning;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Extraction;

public static class FinancialDocumentExtractionServiceCollectionExtensions
{
    public static IServiceCollection AddFinancialDocumentExtraction(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LlmOptions>(
            configuration.GetSection(LlmOptions.SectionName));
        services.Configure<FinancialMetricsExtractionOptions>(
            configuration.GetSection(FinancialMetricsExtractionOptions.SectionName));
        services.AddScoped<FinancialDocumentExtractionResponseParser>();

        var llmOptions = configuration
            .GetSection(LlmOptions.SectionName)
            .Get<LlmOptions>() ?? new LlmOptions();
        var extractionOptions = configuration
            .GetSection(FinancialMetricsExtractionOptions.SectionName)
            .Get<FinancialMetricsExtractionOptions>() ??
            new FinancialMetricsExtractionOptions();

        if (!extractionOptions.SemanticEnrichmentEnabled ||
            !llmOptions.Enabled)
        {
            services.AddScoped<
                IFinancialDocumentExtractionAgent,
                UnavailableFinancialDocumentExtractionAgent>();

            return services;
        }

        ValidateOptions(llmOptions);

        services.AddScoped<
            IFinancialDocumentExtractionAgent,
            SemanticKernelFinancialDocumentExtractionAgent>();

        return services;
    }

    private static void ValidateOptions(LlmOptions options)
    {
        if (!string.Equals(
                options.Provider,
                "OpenAI",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Llm:Provider '{options.Provider}' is not supported. Supported provider: OpenAI.");
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
                "FinancialMetricsExtraction:SemanticEnrichmentEnabled and " +
                "Llm:Enabled are true but required configuration is missing: " +
                $"{string.Join(", ", missingRequiredValues)}.");
        }

        if (string.IsNullOrWhiteSpace(options.ServiceId))
        {
            throw new InvalidOperationException(
                "Llm:ServiceId is required when " +
                "FinancialMetricsExtraction:SemanticEnrichmentEnabled and " +
                "Llm:Enabled are true.");
        }
    }
}
