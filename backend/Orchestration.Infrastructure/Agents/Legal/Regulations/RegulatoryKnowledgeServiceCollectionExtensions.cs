using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Legal.Cnv;
using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

namespace Orchestration.Infrastructure.Agents.Legal.Regulations;

public static class RegulatoryKnowledgeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the bootstrap-scoped CNV MCP capability. Restart the host after changing this configuration.
    /// </summary>
    public static IServiceCollection AddRegulatoryKnowledgeSource(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddOptions<CnvRegulationMcpOptions>()
            .Bind(configuration.GetSection(CnvRegulationMcpOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CnvRegulationMcpOptions>, CnvRegulationMcpOptionsValidator>();
        services.AddSingleton<ICnvRegulationMcpClient, CnvRegulationStdioMcpClient>();
        services.AddSingleton<ICnvRegulationMcpProbe, CnvRegulationMcpProbe>();
        services.AddScoped<CnvRegulatoryHitEnricher>();
        services.AddSingleton<
            ILegalCnvQueryStrategy,
            FinancialAnalysisLegalCnvQueryStrategy>();
        services.AddHealthChecks().AddCheck<CnvRegulationMcpHealthCheck>("cnv_mcp", tags: ["ready"]);

        var options = configuration.GetSection(CnvRegulationMcpOptions.SectionName)
            .Get<CnvRegulationMcpOptions>() ?? new CnvRegulationMcpOptions();

        if (options.Enabled)
        {
            services.AddScoped<IRegulatoryKnowledgeSource, McpRegulatoryKnowledgeSource>();
        }
        else if (environment.IsDevelopment() || environment.IsEnvironment("Test"))
        {
            services.AddScoped<IRegulatoryKnowledgeSource, MockRegulatoryKnowledgeSource>();
        }
        else
        {
            services.AddScoped<IRegulatoryKnowledgeSource, UnavailableRegulatoryKnowledgeSource>();
        }

        return services;
    }
}
