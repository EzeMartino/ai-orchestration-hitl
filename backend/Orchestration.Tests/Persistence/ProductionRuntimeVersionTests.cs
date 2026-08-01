using System.Xml.Linq;
using FluentAssertions;

namespace Orchestration.Tests.Persistence;

public sealed class ProductionRuntimeVersionTests
{
    [Fact]
    public void ApiProject_RequiresPatchedProductionRuntime()
    {
        var project = XDocument.Load(FindApiProjectFile());
        var configuredVersion = project
            .Descendants("RuntimeFrameworkVersion")
            .Select(element => element.Value)
            .SingleOrDefault();

        configuredVersion.Should().NotBeNullOrWhiteSpace();
        Version.TryParse(configuredVersion, out var runtimeVersion)
            .Should()
            .BeTrue();
        runtimeVersion.Should().BeGreaterThanOrEqualTo(new Version(10, 0, 10));
    }

    private static string FindApiProjectFile()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "Orchestration.Api",
                "Orchestration.Api.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "Could not locate backend/Orchestration.Api/Orchestration.Api.csproj "
            + "from the test assembly directory.");
    }
}
