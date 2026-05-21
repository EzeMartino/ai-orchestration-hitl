using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Orchestration.Application.Agents.Data;

namespace Orchestration.Tests.Agents.Data;

public sealed class DataAgentOptionsTests
{
    [Fact]
    public void Defaults_Should_not_enable_fixture_metrics_fallback()
    {
        var options = new DataAgentOptions();

        options.UseFixtureMetricsFallback.Should().BeFalse();
    }

    [Fact]
    public void Base_config_Should_not_enable_fixture_metrics_fallback()
    {
        var options = LoadOptions("appsettings.json");

        options.UseFixtureMetricsFallback.Should().BeFalse();
    }

    [Fact]
    public void Explicit_config_Should_enable_fixture_metrics_fallback()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataAgent:UseFixtureMetricsFallback"] = "true"
            })
            .Build();
        var options = LoadOptions(configuration);

        options.UseFixtureMetricsFallback.Should().BeTrue();
    }

    private static DataAgentOptions LoadOptions(
        string fileName)
    {
        var apiDirectory = ResolveApiDirectory();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiDirectory)
            .AddJsonFile(fileName, optional: false)
            .Build();

        return LoadOptions(configuration);
    }

    private static DataAgentOptions LoadOptions(
        IConfiguration configuration)
    {
        var useFixtureMetricsFallback = bool.TryParse(
            configuration["DataAgent:UseFixtureMetricsFallback"],
            out var parsed
        ) && parsed;

        return new DataAgentOptions
        {
            UseFixtureMetricsFallback = useFixtureMetricsFallback
        };
    }

    private static string ResolveApiDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "Orchestration.Api"
            );

            if (File.Exists(Path.Combine(candidate, "appsettings.json")))
            {
                return candidate;
            }

            candidate = Path.Combine(
                directory.FullName,
                "backend",
                "Orchestration.Api"
            );

            if (File.Exists(Path.Combine(candidate, "appsettings.json")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Orchestration.Api appsettings directory could not be resolved."
        );
    }
}
