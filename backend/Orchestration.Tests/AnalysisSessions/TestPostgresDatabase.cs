using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orchestration.Infrastructure.Persistence;

namespace Orchestration.Tests.AnalysisSessions;

internal sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TestPostgresDatabase.ConnectionVariable)))
        {
            Skip = "Set ORCHESTRATION_TEST_POSTGRES to a disposable PostgreSQL server to run relational checks.";
        }
    }
}

internal sealed class TestPostgresDatabase : IAsyncDisposable
{
    internal const string ConnectionVariable = "ORCHESTRATION_TEST_POSTGRES";
    private readonly string _adminConnection;
    private readonly string _databaseName = "ponytail_test_" + Guid.NewGuid().ToString("N");
    private readonly DbContextOptions<OrchestrationDbContext> _options;

    private TestPostgresDatabase(string adminConnection)
    {
        _adminConnection = adminConnection;
        var connection = new NpgsqlConnectionStringBuilder(adminConnection)
        {
            Database = _databaseName,
            Pooling = false
        };
        _options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseNpgsql(connection.ConnectionString).Options;
    }

    public static async Task<TestPostgresDatabase> CreateAsync()
    {
        var database = new TestPostgresDatabase(
            Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException("PostgreSQL test server is not configured."));
        await using var connection = new NpgsqlConnection(database._adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {database._databaseName}", connection);
        await command.ExecuteNonQueryAsync();
        try
        {
            await using var context = database.CreateContext();
            await context.Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public OrchestrationDbContext CreateContext() => new(_options);

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE {_databaseName} WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
