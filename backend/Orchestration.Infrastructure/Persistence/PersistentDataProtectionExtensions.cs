using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Orchestration.Infrastructure.Persistence;

public static class PersistentDataProtectionExtensions
{
    public static IServiceCollection AddPersistentDataProtection(
        this IServiceCollection services)
    {
        services.AddDataProtection()
            .SetApplicationName("ai-orchestration-hitl")
            .PersistKeysToDbContext<OrchestrationDbContext>();

        return services;
    }
}
