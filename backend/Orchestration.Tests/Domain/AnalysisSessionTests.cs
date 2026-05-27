using System;
using Orchestration.Domain.AnalysisSessions;

namespace Orchestration.Tests.Domain;

public class AnalysisSessionTests
{
    [Fact]
    public void Create_Should_Set_UserId()
    {
        var userId = Guid.NewGuid();
        var session = AnalysisSession.Create(userId);
        Assert.Equal(userId, session.UserId);
    }

    [Fact]
    public void Create_Should_Throw_ArgumentException_When_UserId_Is_Empty()
    {
        var action = () => AnalysisSession.Create(Guid.Empty);
        Assert.Throws<ArgumentException>(action);
    }
}
