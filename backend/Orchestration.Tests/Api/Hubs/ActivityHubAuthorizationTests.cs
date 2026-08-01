using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Orchestration.Api.Hubs;

namespace Orchestration.Tests.Api.Hubs;

public sealed class ActivityHubAuthorizationTests
{
    [Fact]
    public void ActivityHub_Should_require_authenticated_user()
    {
        var authorizeAttributes = typeof(ActivityHub)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true);

        Assert.NotEmpty(authorizeAttributes);
    }
}
