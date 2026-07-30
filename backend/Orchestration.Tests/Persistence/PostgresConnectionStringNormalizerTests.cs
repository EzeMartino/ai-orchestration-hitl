using FluentAssertions;
using Npgsql;
using Orchestration.Infrastructure.Persistence;

namespace Orchestration.Tests.Persistence;

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

    [Fact]
    public void Normalize_ShouldUseDefaultPostgreSqlPort_WhenUriOmitsPort()
    {
        var result = PostgresConnectionStringNormalizer.Normalize(
            "postgres://user:password@dpg.internal/orchestration");

        new NpgsqlConnectionStringBuilder(result).Port.Should().Be(5432);
    }

    [Fact]
    public void Normalize_ShouldRetainExplicitPort()
    {
        var result = PostgresConnectionStringNormalizer.Normalize(
            "postgres://user:password@dpg.internal:5434/orchestration");

        new NpgsqlConnectionStringBuilder(result).Port.Should().Be(5434);
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
        const string connectionString = "Host=localhost;Port=5432;Database=orchestration;Username=user;Password=p://a";

        PostgresConnectionStringNormalizer.Normalize(connectionString).Should().Be(connectionString);
    }

    [Fact]
    public void Normalize_ShouldRejectUnsupportedUriScheme()
    {
        var action = () => PostgresConnectionStringNormalizer.Normalize(
            "mysql://user:password@db.internal/orchestration");

        action.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("postgresql://user:password@/orchestration")]
    [InlineData("postgresql://:password@dpg.internal/orchestration")]
    [InlineData("postgresql://user:@dpg.internal/orchestration")]
    [InlineData("postgresql://user:password@dpg.internal/")]
    [InlineData("postgresql://user@dpg.internal/orchestration")]
    public void Normalize_ShouldRejectUriMissingRequiredPartsOrMalformedUserInfo(string value)
    {
        var action = () => PostgresConnectionStringNormalizer.Normalize(value);

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Normalize_ShouldNotExposeSourceConnectionValue_WhenNormalizationFails()
    {
        const string source = "postgresql://user:super-secret-password@/orchestration";
        var action = () => PostgresConnectionStringNormalizer.Normalize(source);

        var exception = action.Should().Throw<InvalidOperationException>().Which;
        exception.ToString().Should().NotContain(source);
        exception.ToString().Should().NotContain("super-secret-password");
    }
}
