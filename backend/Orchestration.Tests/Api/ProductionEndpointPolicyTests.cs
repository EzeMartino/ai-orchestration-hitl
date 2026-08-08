using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;
using Orchestration.Infrastructure.Persistence;

namespace Orchestration.Tests.Api;

public sealed class ProductionEndpointPolicyTests
{
    [Fact]
    public async Task Alive_Production_IsAnonymousAndUsesFixedSafeBody()
    {
        await using var factory = new EndpointPolicyWebApplicationFactory(Environments.Production);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/alive");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        body.Should().Be("Healthy");
        AssertSafeHealthBody(body);
    }

    [Fact]
    public async Task Health_Production_ReturnsHealthyWithoutDependencyDetails()
    {
        await using var factory = new EndpointPolicyWebApplicationFactory(Environments.Production);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Be("Healthy");
        AssertSafeHealthBody(body);
    }

    [Fact]
    public async Task Health_Production_ReturnsServiceUnavailableWithoutDependencyDetails_WhenRequiredDependencyIsUnhealthy()
    {
        await using var factory = new EndpointPolicyWebApplicationFactory(
            Environments.Production,
            mcpIsUnhealthy: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        body.Should().Be("Unhealthy");
        AssertSafeHealthBody(body);
    }

    [Fact]
    public async Task Health_Production_ReturnsServiceUnavailableWithoutDependencyDetails_WhenApplicationDatabaseIsUnhealthy()
    {
        await using var factory = new EndpointPolicyWebApplicationFactory(
            Environments.Production,
            databaseIsUnhealthy: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        body.Should().Be("Unhealthy");
        AssertSafeHealthBody(body);
    }

    [Fact]
    public async Task Alive_Production_RemainsHealthy_WhenReadinessDatabaseIsUnhealthy()
    {
        await using var factory = new EndpointPolicyWebApplicationFactory(
            Environments.Production,
            databaseIsUnhealthy: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/alive");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Be("Healthy");
        AssertSafeHealthBody(body);
    }

    [Fact]
    public async Task HealthChecks_Production_RequiresActualSelfDatabaseAndMcpRegistrations()
    {
        await using var factory = new EndpointPolicyWebApplicationFactory(Environments.Production);
        using var client = factory.CreateClient();

        await client.GetAsync("/health");

        factory.HealthCheckNamesObservedBeforeReplacement.Should().Contain(
            "self",
            "orchestration_db",
            "cnv_mcp");
        factory.HealthCheckNamesAfterReplacement.Should().BeEquivalentTo(
            factory.HealthCheckNamesObservedBeforeReplacement);
    }

    [Fact]
    public async Task Swagger_Production_IsNotExposed()
    {
        await using var factory = new EndpointPolicyWebApplicationFactory(Environments.Production);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Swagger_Development_RemainsAvailable()
    {
        await using var factory = new EndpointPolicyWebApplicationFactory(Environments.Development);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Diagnostics_Production_RequiresAuthentication()
    {
        await using var factory = new EndpointPolicyWebApplicationFactory(Environments.Production);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/diagnostics/cnv-regulation/status");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Diagnostics_Production_AuthenticatedResponseContainsOnlySafeStatusFields()
    {
        await using var factory = new EndpointPolicyWebApplicationFactory(Environments.Production);
        using var client = factory.CreateClient();
        Authenticate(client);

        var response = await client.GetAsync("/api/diagnostics/cnv-regulation/status");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(body);
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["connected", "coldStartCount", "resetCount", "lastError"]);
        body.Should().NotContain("command", "args", "environment", "connection", "policy-secret");
    }

    [Fact]
    public async Task ToolCallingExecute_Production_RemainsNotFoundForAuthenticatedRequests()
    {
        await using var factory = new EndpointPolicyWebApplicationFactory(Environments.Production);
        using var client = factory.CreateClient();
        Authenticate(client);

        var response = await client.PostAsync(
            "/api/diagnostics/tool-calling/execute",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static void Authenticate(HttpClient client) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            EndpointPolicyAuthenticationHandler.AuthenticationScheme,
            "diagnostics");

    private static void AssertSafeHealthBody(string body)
    {
        body.Should().NotContain(
            "orchestration_db",
            "cnv_mcp",
            "required dependency failed",
            "Host=localhost",
            "policy-secret");
    }

    private sealed class EndpointPolicyWebApplicationFactory(
        string environment,
        bool mcpIsUnhealthy = false,
        bool databaseIsUnhealthy = false) : WebApplicationFactory<Program>
    {
        private readonly List<string> _healthCheckNamesObservedBeforeReplacement = [];

        public IReadOnlyList<string> HealthCheckNamesObservedBeforeReplacement =>
            _healthCheckNamesObservedBeforeReplacement;

        public IReadOnlyList<string> HealthCheckNamesAfterReplacement =>
            Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value
                .Registrations.Select(registration => registration.Name)
                .ToArray();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:orchestrationdb", "Host=localhost;Database=orchestration;Username=postgres;Password=policy-secret");
            builder.UseSetting("Cors:AllowedOrigins:0", "https://frontend.example.test");
            builder.UseSetting("Mcp:CnvRegulation:Enabled", "true");
            builder.UseSetting("Mcp:CnvRegulation:Command", "dotnet");
            builder.UseSetting("Mcp:CnvRegulation:Args:0", "--info");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.PostConfigure<HealthCheckServiceOptions>(options =>
                {
                    var self = RequireRegistration(options, "self");
                    var database = RequireRegistration(options, "orchestration_db");
                    var mcp = RequireRegistration(options, "cnv_mcp");
                    var automaticDatabase = RequireRegistration(
                        options,
                        nameof(OrchestrationDbContext));

                    _healthCheckNamesObservedBeforeReplacement.Clear();
                    _healthCheckNamesObservedBeforeReplacement.AddRange(
                        options.Registrations.Select(registration => registration.Name));

                    self.Factory = _ => new HealthyCheck();
                    database.Factory = _ => new ReadinessDependencyCheck(databaseIsUnhealthy);
                    mcp.Factory = _ => new ReadinessDependencyCheck(mcpIsUnhealthy);
                    automaticDatabase.Factory = _ => new ReadinessDependencyCheck(databaseIsUnhealthy);
                });

                services.RemoveAll<ICnvRegulationMcpClient>();
                services.AddSingleton<ICnvRegulationMcpClient, FakeCnvRegulationMcpClient>();

                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = EndpointPolicyAuthenticationHandler.AuthenticationScheme;
                    options.DefaultChallengeScheme = EndpointPolicyAuthenticationHandler.AuthenticationScheme;
                }).AddScheme<AuthenticationSchemeOptions, EndpointPolicyAuthenticationHandler>(
                    EndpointPolicyAuthenticationHandler.AuthenticationScheme,
                    _ => { });
            });
        }

        private static HealthCheckRegistration RequireRegistration(
            HealthCheckServiceOptions options,
            string name) =>
            options.Registrations.SingleOrDefault(registration => registration.Name == name)
            ?? throw new InvalidOperationException(
                $"The production health check registration '{name}' is required for endpoint policy tests.");
    }

    private sealed class EndpointPolicyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string AuthenticationScheme = "EndpointPolicy";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.Authorization.ToString().StartsWith(AuthenticationScheme, StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "endpoint-policy-user")],
                AuthenticationScheme));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, AuthenticationScheme)));
        }
    }

    private sealed class HealthyCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Healthy());
    }

    private sealed class ReadinessDependencyCheck(bool isUnhealthy) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(isUnhealthy
                ? HealthCheckResult.Unhealthy(
                    "required dependency failed: Host=localhost;Password=policy-secret")
                : HealthCheckResult.Healthy());
    }

    private sealed class FakeCnvRegulationMcpClient : ICnvRegulationMcpClient
    {
        public bool IsConnected => true;
        public int ColdStartCount => 0;
        public int ResetCount => 0;
        public string? LastError => null;

        public Task<CnvRegulationSearchResponse> SearchAsync(
            CnvRegulationSearchRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationSearchResponse(request.Query, [], []));

        public Task<CnvRegulationDocumentResponse> GetDocumentAsync(
            CnvRegulationDocumentRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationDocumentResponse(false, null, [], []));

        public Task<CnvRegulationArticleResponse> GetArticleAsync(
            CnvRegulationArticleRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CnvRegulationArticleResponse(false, null, null, 0, []));
    }
}
