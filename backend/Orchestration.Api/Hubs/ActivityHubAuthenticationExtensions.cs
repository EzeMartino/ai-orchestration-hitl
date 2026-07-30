using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;

namespace Orchestration.Api.Hubs;

public static class ActivityHubAuthenticationExtensions
{
    private static readonly PathString ActivityHubPath = new("/hubs/activity");

    public static IServiceCollection AddActivityHubAuthentication(
        this IServiceCollection services)
    {
        services.PostConfigure<BearerTokenOptions>(
            IdentityConstants.BearerScheme,
            options =>
            {
                var previous = options.Events.OnMessageReceived;

                options.Events.OnMessageReceived = async context =>
                {
                    if (previous is not null)
                    {
                        await previous(context);
                    }

                    if (context.Token is not null
                        || context.Request.Headers.ContainsKey("Authorization")
                        || !context.Request.Path.StartsWithSegments(ActivityHubPath)
                        || !context.Request.Query.TryGetValue(
                            "access_token",
                            out var accessTokens)
                        || accessTokens.Count != 1
                        || string.IsNullOrWhiteSpace(accessTokens[0]))
                    {
                        return;
                    }

                    context.Token = accessTokens[0];
                };
            });

        return services;
    }
}
