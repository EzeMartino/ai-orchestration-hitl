using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Orchestration.Infrastructure.Persistence;

public sealed class OrchestrationDatabaseMigrator(IServiceScopeFactory scopeFactory)
    : IOrchestrationDatabaseMigrator
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
