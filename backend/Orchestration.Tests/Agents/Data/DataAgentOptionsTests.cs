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
        options.RequireSessionFinancialMetrics.Should().BeFalse();
        options.AiReviewEnabled.Should().BeFalse();
    }

    [Fact]
    public void Base_config_Should_not_enable_fixture_metrics_fallback()
    {
        var options = LoadOptions("appsettings.json");

        options.UseFixtureMetricsFallback.Should().BeFalse();
        options.RequireSessionFinancialMetrics.Should().BeFalse();
        options.AiReviewEnabled.Should().BeFalse();
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

    [Fact]
    public void Explicit_config_Should_enable_required_session_financial_metrics()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataAgent:RequireSessionFinancialMetrics"] = "true"
            })
            .Build();
        var options = LoadOptions(configuration);

        options.RequireSessionFinancialMetrics.Should().BeTrue();
    }

    [Fact]
    public void Explicit_config_Should_enable_ai_review()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataAgent:AiReviewEnabled"] = "true"
            })
            .Build();
        var options = LoadOptions(configuration);

        options.AiReviewEnabled.Should().BeTrue();
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
        var requireSessionFinancialMetrics = bool.TryParse(
            configuration["DataAgent:RequireSessionFinancialMetrics"],
            out var parsedRequired
        ) && parsedRequired;
        var aiReviewEnabled = bool.TryParse(
            configuration["DataAgent:AiReviewEnabled"],
            out var parsedAiReviewEnabled
        ) && parsedAiReviewEnabled;

        return new DataAgentOptions
        {
            UseFixtureMetricsFallback = useFixtureMetricsFallback,
            RequireSessionFinancialMetrics = requireSessionFinancialMetrics,
            AiReviewEnabled = aiReviewEnabled
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
