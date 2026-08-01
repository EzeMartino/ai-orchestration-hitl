using CnvRegulation.Infrastructure.Persistence;
using FluentAssertions;
using Npgsql;

namespace CnvRegulation.Application.Tests;

[Collection(nameof(EnvironmentVariableTestCollection))]
public sealed class PostgresConnectionStringNormalizerTests
{
    [Fact]
    public void Normalize_ShouldConvertRenderUriAndDecodeCredentials()
    {
        var result = PostgresConnectionStringNormalizer.Normalize(
            "postgresql://render%40user:p%2Fa%3Ass@dpg.internal:5433/orchestration");

        var parsed = new NpgsqlConnectionStringBuilder(result);
        parsed.Host.Should().Be("dpg.internal");
        parsed.Port.Should().Be(5433);
        parsed.Database.Should().Be("orchestration");
        parsed.Username.Should().Be("render@user");
        parsed.Password.Should().Be("p/a:ss");
    }

    [Theory]
    [InlineData("postgres://user:password@dpg.internal/orchestration", 5432)]
    [InlineData("postgresql://user:password@dpg.internal:5434/orchestration", 5434)]
    public void Normalize_ShouldUseExpectedPort(string value, int expectedPort)
    {
        var result = PostgresConnectionStringNormalizer.Normalize(value);

        new NpgsqlConnectionStringBuilder(result).Port.Should().Be(expectedPort);
    }

    [Fact]
    public void Normalize_ShouldDecodeDatabaseName()
    {
        var result = PostgresConnectionStringNormalizer.Normalize(
            "postgresql://user:password@dpg.internal/my%2Ddatabase");

        new NpgsqlConnectionStringBuilder(result).Database.Should().Be("my-database");
    }

    [Fact]
    public void Normalize_ShouldPassThroughNpgsqlKeywordValueConnectionString()
    {
        const string connectionString = "Host=localhost;Database=orchestration;Username=user;Password=p://a";

        PostgresConnectionStringNormalizer.Normalize(connectionString).Should().Be(connectionString);
    }

    [Theory]
    [InlineData("postgresql://%20:password@dpg.internal/orchestration")]
    [InlineData("postgresql://user:%20@dpg.internal/orchestration")]
    public void Normalize_ShouldRejectWhitespaceOnlyDecodedCredentials(string value)
    {
        var action = () => PostgresConnectionStringNormalizer.Normalize(value);

        action.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("postgresql://user:password@dpg.internal/orchestration?sslmode=require")]
    [InlineData("postgresql://user:password@dpg.internal/orchestration#fragment")]
    public void Normalize_ShouldRejectUriQueryOrFragment(string value)
    {
        var action = () => PostgresConnectionStringNormalizer.Normalize(value);

        action.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("mysql://user:password@db.internal/orchestration")]
    [InlineData("postgresql://user:password@/orchestration")]
    [InlineData("postgresql://:password@db.internal/orchestration")]
    [InlineData("postgresql://user:@db.internal/orchestration")]
    [InlineData("postgresql://user:password@db.internal/")]
    [InlineData("postgresql://user@db.internal/orchestration")]
    public void Normalize_ShouldRejectUnsupportedOrMalformedUriWithoutExposingSource(string source)
    {
        var action = () => PostgresConnectionStringNormalizer.Normalize(source);

        var exception = action.Should().Throw<InvalidOperationException>().Which;
        exception.ToString().Should().NotContain(source);
        exception.ToString().Should().NotContain("password");
    }

    [Fact]
    public void Create_ShouldPreferEnvironmentValueAndNormalizeIt()
    {
        const string environmentValue = "postgresql://env-user:env-password@dpg.internal/env-db";
        var previousValue = Environment.GetEnvironmentVariable("CNV_REGULATION_DB_CONNECTION_STRING");

        try
        {
            Environment.SetEnvironmentVariable("CNV_REGULATION_DB_CONNECTION_STRING", environmentValue);

            var result = RegulationDbOptions.Create(
                "Postgres",
                "Host=configuration-host;Database=configuration-db;Username=user;Password=password");

            var parsed = new NpgsqlConnectionStringBuilder(result.ConnectionString);
            parsed.Host.Should().Be("dpg.internal");
            parsed.Database.Should().Be("env-db");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CNV_REGULATION_DB_CONNECTION_STRING", previousValue);
        }
    }

    [Fact]
    public void Create_ShouldKeepBlankConnectionStringAsNull()
    {
        var previousValue = Environment.GetEnvironmentVariable("CNV_REGULATION_DB_CONNECTION_STRING");

        try
        {
            Environment.SetEnvironmentVariable("CNV_REGULATION_DB_CONNECTION_STRING", null);

            RegulationDbOptions.Create("InMemory", " ").ConnectionString.Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("CNV_REGULATION_DB_CONNECTION_STRING", previousValue);
        }
    }

    [Fact]
    public void Normalize_ShouldNotExposeMalformedKeywordValueSource_WhenValidationFails()
    {
        const string source = "Host=localhost;Password=keyword-sentinel-secret;UnsupportedKeyword=value";
        var action = () => PostgresConnectionStringNormalizer.Normalize(source);

        var exception = action.Should().Throw<InvalidOperationException>().Which;
        exception.ToString().Should().NotContain(source);
        exception.ToString().Should().NotContain("keyword-sentinel-secret");
    }
}
