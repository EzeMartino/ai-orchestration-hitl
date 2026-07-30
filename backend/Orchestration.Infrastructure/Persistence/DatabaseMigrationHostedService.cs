using Microsoft.Extensions.Hosting;

namespace Orchestration.Infrastructure.Persistence;

public sealed class DatabaseMigrationHostedService(IOrchestrationDatabaseMigrator migrator)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        migrator.MigrateAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
