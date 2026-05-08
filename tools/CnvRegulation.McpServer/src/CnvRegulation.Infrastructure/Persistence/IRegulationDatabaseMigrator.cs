namespace CnvRegulation.Infrastructure.Persistence;

/// <summary>
/// Applies database schema changes for regulation persistence.
/// </summary>
public interface IRegulationDatabaseMigrator
{
    /// <summary>
    /// Applies pending schema changes.
    /// </summary>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>A task that completes when migration finishes.</returns>
    Task MigrateAsync(CancellationToken cancellationToken);
}
