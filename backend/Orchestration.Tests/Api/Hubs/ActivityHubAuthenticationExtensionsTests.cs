using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Orchestration.Api.Hubs;

namespace Orchestration.Tests.Api.Hubs;

public sealed class ActivityHubAuthenticationExtensionsTests
{
    [Fact]
    public async Task OnMessageReceived_ActivityHubPathWithOneToken_AcceptsToken()
    {
        var context = await InvokeHandlerAsync(
            "/hubs/activity",
            queryValues: ["opaque-identity-token"]);

        Assert.Equal("opaque-identity-token", context.Token);
    }

    [Fact]
    public async Task OnMessageReceived_ActivityHubNegotiatePathWithOneToken_AcceptsToken()
    {
        var context = await InvokeHandlerAsync(
            "/hubs/activity/negotiate",
            queryValues: ["opaque-identity-token"]);

        Assert.Equal("opaque-identity-token", context.Token);
    }

    [Fact]
    public async Task OnMessageReceived_OrdinaryApiPath_IgnoresQueryToken()
    {
        var context = await InvokeHandlerAsync(
            "/api/analysis-sessions",
            queryValues: ["opaque-identity-token"]);

        Assert.Null(context.Token);
    }

    [Theory]
    [InlineData("/hubs/activity-stream")]
    [InlineData("/hubs/activityevil")]
    public async Task OnMessageReceived_SimilarPrefixWithoutPathSegment_IgnoresQueryToken(
        string path)
    {
        var context = await InvokeHandlerAsync(
            path,
            queryValues: ["opaque-identity-token"]);

        Assert.Null(context.Token);
    }

    [Fact]
    public async Task OnMessageReceived_MissingQueryToken_IgnoresQuery()
    {
        var context = await InvokeHandlerAsync("/hubs/activity");

        Assert.Null(context.Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task OnMessageReceived_BlankQueryToken_IgnoresQuery(string token)
    {
        var context = await InvokeHandlerAsync(
            "/hubs/activity",
            queryValues: [token]);

        Assert.Null(context.Token);
    }

    [Fact]
    public async Task OnMessageReceived_MultipleQueryTokens_IgnoresQuery()
    {
        var context = await InvokeHandlerAsync(
            "/hubs/activity",
            queryValues: ["first-token", "second-token"]);

        Assert.Null(context.Token);
    }

    [Fact]
    public async Task OnMessageReceived_ExistingContextToken_DoesNotOverwriteToken()
    {
        var context = await InvokeHandlerAsync(
            "/hubs/activity",
            queryValues: ["query-token"],
            existingToken: "previous-token");

        Assert.Equal("previous-token", context.Token);
    }

    [Fact]
    public async Task OnMessageReceived_AuthorizationHeader_DoesNotUseQueryToken()
    {
        var context = await InvokeHandlerAsync(
            "/hubs/activity",
            queryValues: ["query-token"],
            authorizationHeader: "Bearer header-token");

        Assert.Null(context.Token);
    }

    [Fact]
    public async Task OnMessageReceived_PreviouslyConfiguredHandler_IsInvokedAndPreserved()
    {
        var previousWasInvoked = false;

        var context = await InvokeHandlerAsync(
            "/hubs/activity",
            queryValues: ["query-token"],
            previousHandler: context =>
            {
                previousWasInvoked = true;
                context.Token = "previous-handler-token";
                return Task.CompletedTask;
            });

        Assert.True(previousWasInvoked);
        Assert.Equal("previous-handler-token", context.Token);
    }

    private static async Task<MessageReceivedContext> InvokeHandlerAsync(
        string path,
        string[]? queryValues = null,
        string? existingToken = null,
        string? authorizationHeader = null,
        Func<MessageReceivedContext, Task>? previousHandler = null)
    {
        var services = new ServiceCollection();

        services
            .AddAuthentication()
            .AddBearerToken(
                IdentityConstants.BearerScheme,
                options =>
                {
                    if (previousHandler is not null)
                    {
                        options.Events.OnMessageReceived = previousHandler;
                    }
                });
        services.AddActivityHubAuthentication();

        await using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptionsMonitor<BearerTokenOptions>>()
            .Get(IdentityConstants.BearerScheme);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;

        if (queryValues is not null)
        {
            httpContext.Request.Query = new QueryCollection(
                new Dictionary<string, StringValues>
                {
                    ["access_token"] = new(queryValues)
                });
        }

        if (authorizationHeader is not null)
        {
            httpContext.Request.Headers.Authorization = authorizationHeader;
        }

        var authenticationScheme = new AuthenticationScheme(
            IdentityConstants.BearerScheme,
            displayName: null,
            typeof(AuthenticationHandler<BearerTokenOptions>));
        var messageReceivedContext = new MessageReceivedContext(
            httpContext,
            authenticationScheme,
            options)
        {
            Token = existingToken
        };

        await options.Events.OnMessageReceived(messageReceivedContext);

        return messageReceivedContext;
    }
}
