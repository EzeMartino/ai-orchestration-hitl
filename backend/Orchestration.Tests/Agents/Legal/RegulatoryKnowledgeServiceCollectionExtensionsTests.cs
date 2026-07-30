using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Legal.Regulations;
using Orchestration.Infrastructure.Agents.Legal.Regulations;

namespace Orchestration.Tests.Agents.Legal;

public sealed class RegulatoryKnowledgeServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData("Development", false, false, typeof(MockRegulatoryKnowledgeSource))]
    [InlineData("Test", false, false, typeof(MockRegulatoryKnowledgeSource))]
    [InlineData("Production", false, false, typeof(UnavailableRegulatoryKnowledgeSource))]
    [InlineData("Development", true, true, typeof(McpRegulatoryKnowledgeSource))]
    [InlineData("Production", true, true, typeof(McpRegulatoryKnowledgeSource))]
    public void AddRegulatoryKnowledgeSource_Should_select_exact_bootstrap_source(
        string environmentName,
        bool enabled,
        bool required,
        Type expectedImplementation)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mcp:CnvRegulation:Enabled"] = enabled.ToString(),
                ["Mcp:CnvRegulation:Required"] = required.ToString(),
                ["Mcp:CnvRegulation:Args:0"] = "run"
            })
            .Build();

        services.AddRegulatoryKnowledgeSource(
            configuration,
            new TestHostEnvironment(environmentName));

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(IRegulatoryKnowledgeSource))
            .ToArray();
        registrations.Should().ContainSingle();
        registrations[0].ImplementationType.Should().Be(expectedImplementation);
    }

    [Fact]
    public void AddRegulatoryKnowledgeSource_Should_register_the_single_ready_health_check()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        services.AddRegulatoryKnowledgeSource(
            configuration,
            new TestHostEnvironment(Environments.Production));

        using var provider = services.BuildServiceProvider();
        var registrations = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations;

        var registration = registrations.Should()
            .ContainSingle(registration => registration.Name == "cnv_mcp")
            .Which;
        registration.Tags.Should().ContainSingle().Which.Should().Be("ready");
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
