namespace Orchestration.Infrastructure.Persistence;

public interface IOrchestrationDatabaseMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken);
}
