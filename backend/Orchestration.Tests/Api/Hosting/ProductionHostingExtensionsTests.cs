using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orchestration.Api.Hosting;

namespace Orchestration.Tests.Api.Hosting;

public sealed class ProductionHostingExtensionsTests
{
    [Fact]
    public void AddProductionHosting_UsesTheSameValidatedOriginsForCorsAndWebSockets()
    {
        var services = new ServiceCollection();
        services.AddProductionHosting(Configuration(
            ("Cors:AllowedOrigins:0", "https://frontend.example.com/"),
            ("RenderProxy:Enabled", "false")),
            new TestHostEnvironment("Production"));
        using var provider = services.BuildServiceProvider();
        var corsPolicy = provider.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy("Frontend");
        var webSocketOptions = provider.GetRequiredService<IOptions<WebSocketOptions>>().Value;

        corsPolicy.Should().NotBeNull();
        corsPolicy!.Origins.Should().Equal(webSocketOptions.AllowedOrigins);
        corsPolicy.SupportsCredentials.Should().BeTrue();
        webSocketOptions.AllowedOrigins.Should().Equal("https://frontend.example.com");
    }

    [Fact]
    public void AddProductionHosting_WhenRenderProxyEnabled_TrustsOnlyRenderForwardedHeaders()
    {
        var services = new ServiceCollection();
        services.AddProductionHosting(Configuration(
            ("Cors:AllowedOrigins:0", "https://frontend.example.com"),
            ("RenderProxy:Enabled", "true")),
            new TestHostEnvironment("Production"));
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        options.ForwardedHeaders.Should().Be(
            ForwardedHeaders.XForwardedFor |
            ForwardedHeaders.XForwardedProto |
            ForwardedHeaders.XForwardedHost);
        options.KnownIPNetworks.Should().BeEmpty();
        options.KnownProxies.Should().BeEmpty();
    }

    [Fact]
    public void AddProductionHosting_WhenRenderProxyDisabled_DoesNotRelaxProxyTrust()
    {
        var services = new ServiceCollection();
        services.AddProductionHosting(Configuration(
            ("Cors:AllowedOrigins:0", "https://frontend.example.com"),
            ("RenderProxy:Enabled", "false")),
            new TestHostEnvironment("Production"));
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        options.ForwardedHeaders.Should().Be(ForwardedHeaders.None);
        options.KnownIPNetworks.Should().NotBeEmpty();
        options.KnownProxies.Should().NotBeEmpty();
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(value => value.Key, value => (string?)value.Value))
            .Build();

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Orchestration.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
