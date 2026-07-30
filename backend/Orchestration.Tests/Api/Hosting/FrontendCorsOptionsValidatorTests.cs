using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orchestration.Api.Hosting;

namespace Orchestration.Tests.Api.Hosting;

public sealed class FrontendCorsOptionsValidatorTests
{
    [Fact]
    public void Validate_ProductionWithNoOrigins_FailsClosed()
    {
        var result = CreateValidator("Production").Validate(null, new FrontendCorsOptions());

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle()
            .Which.Should().Be("Cors:AllowedOrigins must contain at least one HTTPS origin outside Development.");
    }

    [Fact]
    public void ResolveAllowedOrigins_DevelopmentWithNoOrigins_UsesExplicitLocalhostFallback()
    {
        var origins = FrontendCorsOptionsValidator.ResolveAllowedOrigins(
            new FrontendCorsOptions(),
            isDevelopment: true);

        origins.Should().Equal("http://localhost:5173", "https://localhost:5173");
    }

    [Fact]
    public void Validate_DevelopmentWithExplicitLocalhostHttpOrigin_Succeeds()
    {
        var result = CreateValidator("Development").Validate(null, new FrontendCorsOptions
        {
            AllowedOrigins = ["http://localhost:5173"]
        });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_DevelopmentWithExplicitNonLocalHttpOrigin_Fails()
    {
        var result = CreateValidator("Development").Validate(null, new FrontendCorsOptions
        {
            AllowedOrigins = ["http://frontend.example.com"]
        });

        result.Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData("http://frontend.example.com")]
    [InlineData("https://*.example.com")]
    [InlineData("https://frontend.example.com/app")]
    [InlineData("https://frontend.example.com?tenant=one")]
    [InlineData("https://frontend.example.com#fragment")]
    [InlineData("https://user@frontend.example.com")]
    [InlineData("/relative")]
    public void Validate_WithUnsafeOrigin_Fails(string origin)
    {
        var result = CreateValidator("Production").Validate(null, new FrontendCorsOptions
        {
            AllowedOrigins = [origin]
        });

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_OriginsThatDuplicateAfterNormalization_Fails()
    {
        var result = CreateValidator("Production").Validate(null, new FrontendCorsOptions
        {
            AllowedOrigins = ["https://frontend.example.com", "https://frontend.example.com/"]
        });

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle()
            .Which.Should().Be("Cors:AllowedOrigins contains duplicate origin 'https://frontend.example.com'.");
    }

    [Fact]
    public void ResolveAllowedOrigins_NormalizesTrailingSlash()
    {
        var origins = FrontendCorsOptionsValidator.ResolveAllowedOrigins(new FrontendCorsOptions
        {
            AllowedOrigins = ["https://frontend.example.com/"]
        }, isDevelopment: false);

        origins.Should().Equal("https://frontend.example.com");
    }

    private static FrontendCorsOptionsValidator CreateValidator(string environmentName) =>
        new(new TestHostEnvironment(environmentName));

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Orchestration.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
