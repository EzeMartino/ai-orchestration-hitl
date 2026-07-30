using Npgsql;

namespace Orchestration.Infrastructure.Persistence;

public static class PostgresConnectionStringNormalizer
{
    private const string InvalidConnectionStringMessage = "Invalid PostgreSQL connection string.";

    public static string Normalize(string value)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw InvalidConnectionString();
            }

            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                if (!IsPostgresScheme(uri.Scheme))
                {
                    throw InvalidConnectionString();
                }

                return NormalizeUri(uri);
            }

            if (value.Contains("://", StringComparison.Ordinal))
            {
                throw InvalidConnectionString();
            }

            _ = new NpgsqlConnectionStringBuilder(value);
            return value;
        }
        catch (InvalidOperationException exception)
            when (string.Equals(exception.Message, InvalidConnectionStringMessage, StringComparison.Ordinal))
        {
            throw;
        }
        catch
        {
            throw InvalidConnectionString();
        }
    }

    private static string NormalizeUri(Uri uri)
    {
        var credentials = uri.UserInfo.Split(':', 2);
        var database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));

        if (string.IsNullOrWhiteSpace(uri.Host)
            || credentials.Length != 2
            || string.IsNullOrWhiteSpace(credentials[0])
            || string.IsNullOrWhiteSpace(credentials[1])
            || string.IsNullOrWhiteSpace(database)
            || database.Contains('/', StringComparison.Ordinal))
        {
            throw InvalidConnectionString();
        }

        var port = uri.IsDefaultPort ? 5432 : uri.Port;
        if (port is <= 0 or > 65535)
        {
            throw InvalidConnectionString();
        }

        return new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = port,
            Database = database,
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = Uri.UnescapeDataString(credentials[1])
        }.ConnectionString;
    }

    private static bool IsPostgresScheme(string scheme) =>
        string.Equals(scheme, "postgres", StringComparison.OrdinalIgnoreCase)
        || string.Equals(scheme, "postgresql", StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException InvalidConnectionString() =>
        new(InvalidConnectionStringMessage);
}
