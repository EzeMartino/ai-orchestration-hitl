using System;
using Microsoft.AspNetCore.Authorization;
using Orchestration.Api.Controllers;
using Xunit;

namespace Orchestration.Tests.Api;

public class AnalysisSessionsControllerSecurityTests
{
    [Fact]
    public void Controller_Should_Have_AuthorizeAttribute()
    {
        var type = typeof(AnalysisSessionsController);
        var attributes = type.GetCustomAttributes(typeof(AuthorizeAttribute), true);
        Assert.Single(attributes);
    }
}
