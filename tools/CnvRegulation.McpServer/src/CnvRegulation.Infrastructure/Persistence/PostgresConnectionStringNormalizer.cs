using Npgsql;

namespace CnvRegulation.Infrastructure.Persistence;

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

            if (StartsWithUriScheme(value))
            {
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                    || !IsPostgresScheme(uri.Scheme))
                {
                    throw InvalidConnectionString();
                }

                return NormalizeUri(uri);
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
        var username = credentials.Length == 2
            ? Uri.UnescapeDataString(credentials[0])
            : string.Empty;
        var password = credentials.Length == 2
            ? Uri.UnescapeDataString(credentials[1])
            : string.Empty;
        var database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));

        if (!string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || string.IsNullOrWhiteSpace(uri.Host)
            || credentials.Length != 2
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)
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
            Username = username,
            Password = password
        }.ConnectionString;
    }

    private static bool IsPostgresScheme(string scheme) =>
        string.Equals(scheme, "postgres", StringComparison.OrdinalIgnoreCase)
        || string.Equals(scheme, "postgresql", StringComparison.OrdinalIgnoreCase);

    private static bool StartsWithUriScheme(string value)
    {
        var separatorIndex = value.IndexOf("://", StringComparison.Ordinal);
        if (separatorIndex <= 0 || !char.IsLetter(value[0]))
        {
            return false;
        }

        for (var index = 1; index < separatorIndex; index++)
        {
            if (!char.IsLetterOrDigit(value[index])
                && value[index] is not '+' and not '-' and not '.')
            {
                return false;
            }
        }

        return true;
    }

    private static InvalidOperationException InvalidConnectionString() =>
        new(InvalidConnectionStringMessage);
}
