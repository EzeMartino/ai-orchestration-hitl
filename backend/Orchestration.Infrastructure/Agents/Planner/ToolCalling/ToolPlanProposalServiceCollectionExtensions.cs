using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Planner.ToolCalling;
using Orchestration.Infrastructure.Agents.Legal.Regulations;
using Orchestration.Infrastructure.Agents.Planner.Reasoning;

namespace Orchestration.Infrastructure.Agents.Planner.ToolCalling;

public static class ToolPlanProposalServiceCollectionExtensions
{
    private const string DisabledLegalMcpReason =
        "Plan-driven legal tool execution requires Mcp:CnvRegulation:Enabled=true at application startup; restart is required after enabling it.";

    public static IServiceCollection AddToolPlanProposal(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var toolCallingOptions = CreateToolCallingOptions(configuration);
        var llmOptions = configuration
            .GetSection(LlmOptions.SectionName)
            .Get<LlmOptions>() ?? new LlmOptions();
        var cnvRegulationOptions = GetCnvRegulationOptions(configuration);
        var legalMcpEnabledAtStartup = cnvRegulationOptions.Enabled;

        if (RequiresLegalMcp(toolCallingOptions) &&
            !legalMcpEnabledAtStartup)
        {
            throw new InvalidOperationException(DisabledLegalMcpReason);
        }

        services.Configure<ToolCallingOptions>(
            configuration.GetSection(ToolCallingOptions.SectionName));
        services.PostConfigure<ToolCallingOptions>(options =>
            options.AllowedTools = ResolveAllowedTools(configuration));
        services.AddOptions<ToolCallingOptions>()
            .Validate(
                options =>
                    !RequiresLegalMcp(options) ||
                    legalMcpEnabledAtStartup,
                DisabledLegalMcpReason);
        services.Configure<LlmOptions>(
            configuration.GetSection(LlmOptions.SectionName)
        );

        services.AddScoped<DeterministicToolPlanProposalService>(provider =>
            new DeterministicToolPlanProposalService(
                provider.GetRequiredService<
                    IOptionsSnapshot<ToolCallingOptions>>().Value
            )
        );
        services.AddScoped<SemanticKernelToolPlanResponseParser>();

        if (toolCallingOptions.Enabled && llmOptions.Enabled)
        {
            services.AddScoped<IToolPlanProposalService>(provider =>
                new SemanticKernelToolPlanProposalService(
                    provider.GetRequiredService<IOptions<LlmOptions>>(),
                    provider.GetRequiredService<
                        IOptionsSnapshot<ToolCallingOptions>>(),
                    provider.GetRequiredService<
                        DeterministicToolPlanProposalService>(),
                    provider.GetRequiredService<
                        SemanticKernelToolPlanResponseParser>(),
                    provider.GetService<
                        ILogger<SemanticKernelToolPlanProposalService>>())
            );
        }
        else
        {
            services.AddScoped<IToolPlanProposalService>(provider =>
                provider.GetRequiredService<DeterministicToolPlanProposalService>()
            );
        }

        return services;
    }

    private static ToolCallingOptions CreateToolCallingOptions(
        IConfiguration configuration)
    {
        var options = new ToolCallingOptions();

        configuration.GetSection(ToolCallingOptions.SectionName).Bind(options);
        options.AllowedTools = ResolveAllowedTools(configuration);

        return options;
    }

    private static bool RequiresLegalMcp(ToolCallingOptions options)
    {
        return options.Enabled &&
            options.ExecutionMode == ToolCallingExecutionMode.PlanDriven &&
            options.AllowedTools.Any(tool => string.Equals(
                tool,
                PlannerToolCatalog.SearchCnvRegulationName,
                StringComparison.OrdinalIgnoreCase));
    }

    private static CnvRegulationMcpOptions GetCnvRegulationOptions(
        IConfiguration configuration)
    {
        return configuration
            .GetSection(CnvRegulationMcpOptions.SectionName)
            .Get<CnvRegulationMcpOptions>() ?? new CnvRegulationMcpOptions();
    }

    private static string[] ResolveAllowedTools(
        IConfiguration configuration)
    {
        var path = $"{ToolCallingOptions.SectionName}:AllowedTools";

        if (configuration is IConfigurationRoot root)
        {
            foreach (var provider in root.Providers.Reverse())
            {
                if (TryResolveProviderAllowlist(provider, path, out var allowedTools))
                {
                    return allowedTools;
                }
            }
        }

        var section = configuration.GetSection(path);
        if (section.Exists())
        {
            return section.GetChildren()
                .Select(child => (
                    Child: child,
                    IsIndex: int.TryParse(child.Key, out var index),
                    Index: index))
                .Where(item => item.IsIndex && item.Index >= 0)
                .OrderBy(item => item.Index)
                .Select(item => item.Child.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray();
        }

        return new ToolCallingOptions().AllowedTools;
    }

    private static bool TryResolveProviderAllowlist(
        IConfigurationProvider provider,
        string path,
        out string[] allowedTools)
    {
        var indexedChildren = provider.GetChildKeys([], path)
            .Select(key => (
                Key: key,
                IsIndex: int.TryParse(key, out var index),
                Index: index))
            .Where(item => item.IsIndex && item.Index >= 0)
            .DistinctBy(item => item.Index)
            .OrderBy(item => item.Index)
            .ToArray();
        var declaresRoot = provider.TryGet(path, out _);

        if (!declaresRoot && indexedChildren.Length == 0)
        {
            allowedTools = [];
            return false;
        }

        allowedTools = indexedChildren
            .Select(item => provider.TryGet(
                $"{path}:{item.Key}",
                out var value)
                ? value
                : null)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();

        return true;
    }
}
