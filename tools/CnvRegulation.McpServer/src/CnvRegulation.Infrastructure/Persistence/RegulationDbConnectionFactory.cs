using Npgsql;

namespace CnvRegulation.Infrastructure.Persistence;

/// <summary>
/// Creates PostgreSQL connections for regulation persistence.
/// </summary>
public sealed class RegulationDbConnectionFactory(RegulationDbOptions options)
{
    /// <summary>
    /// Opens a PostgreSQL connection.
    /// </summary>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>An open database connection.</returns>
    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                "PostgreSQL storage requires RegulationDb:ConnectionString or CNV_REGULATION_DB_CONNECTION_STRING.");
        }

        var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        return connection;
    }
}
