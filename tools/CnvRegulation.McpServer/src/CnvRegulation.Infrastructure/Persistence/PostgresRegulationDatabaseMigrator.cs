using System.Reflection;
using Dapper;
using Npgsql;

namespace CnvRegulation.Infrastructure.Persistence;

/// <summary>
/// Applies embedded PostgreSQL schema scripts.
/// </summary>
public sealed class PostgresRegulationDatabaseMigrator(
    RegulationDbConnectionFactory connectionFactory,
    RegulationDbOptions options) : IRegulationDatabaseMigrator
{
    /// <inheritdoc />
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await EnsureTargetDatabaseExistsAsync(cancellationToken).ConfigureAwait(false);

        var schema = ReadEmbeddedSchema("001_create_regulation_tables.sql");
        await using var connection = await connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            schema,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private async Task EnsureTargetDatabaseExistsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return;
        }

        var builder = new NpgsqlConnectionStringBuilder(options.ConnectionString);
        var targetDatabase = builder.Database;
        if (string.IsNullOrWhiteSpace(targetDatabase)
            || string.Equals(targetDatabase, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        builder.Database = "postgres";

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @DatabaseName);",
            new { DatabaseName = targetDatabase },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (exists)
        {
            return;
        }

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                $"CREATE DATABASE {QuoteIdentifier(targetDatabase)} TEMPLATE template0;",
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (PostgresException exception)
            when (string.Equals(exception.SqlState, PostgresErrorCodes.DuplicateDatabase, StringComparison.Ordinal))
        {
            // Another process created the database after the existence check.
        }
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

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
