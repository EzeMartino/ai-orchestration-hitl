namespace CnvRegulation.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL database settings for CNV regulation persistence.
/// </summary>
public sealed class RegulationDbOptions
{
    /// <summary>
    /// Gets the configured storage provider.
    /// </summary>
    public string Provider { get; init; } = "InMemory";

    /// <summary>
    /// Gets the PostgreSQL connection string.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Creates options from explicit values and the supported environment variable.
    /// </summary>
    /// <param name="provider">The configured provider.</param>
    /// <param name="connectionString">The configured connection string.</param>
    /// <returns>The resolved database options.</returns>
    public static RegulationDbOptions Create(string? provider, string? connectionString)
    {
        var environmentConnectionString = Environment.GetEnvironmentVariable(
            "CNV_REGULATION_DB_CONNECTION_STRING");

        return new RegulationDbOptions
        {
            Provider = string.IsNullOrWhiteSpace(provider) ? "InMemory" : provider.Trim(),
            ConnectionString = string.IsNullOrWhiteSpace(environmentConnectionString)
                ? connectionString
                : environmentConnectionString
        };
    }

    /// <summary>
    /// Gets a value indicating whether PostgreSQL is configured.
    /// </summary>
    public bool UsePostgres =>
        string.Equals(Provider, "Postgres", StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrWhiteSpace(ConnectionString);
}
