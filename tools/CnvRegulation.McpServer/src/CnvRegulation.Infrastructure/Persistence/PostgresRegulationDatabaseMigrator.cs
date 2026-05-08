using System.Reflection;
using Dapper;

namespace CnvRegulation.Infrastructure.Persistence;

/// <summary>
/// Applies embedded PostgreSQL schema scripts.
/// </summary>
public sealed class PostgresRegulationDatabaseMigrator(
    RegulationDbConnectionFactory connectionFactory) : IRegulationDatabaseMigrator
{
    /// <inheritdoc />
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var schema = ReadEmbeddedSchema("001_create_regulation_tables.sql");
        await using var connection = await connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            schema,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static string ReadEmbeddedSchema(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(fileName, StringComparison.Ordinal));

        if (resourceName is null)
        {
            throw new InvalidOperationException($"Embedded schema resource was not found: {fileName}");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded schema resource could not be opened: {fileName}");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
