using FluentAssertions;

namespace Orchestration.Tests.Agents.Legal;

public sealed class CnvRegulationMcpAppHostConfigurationTests
{
    [Theory]
    [InlineData("Enabled")]
    [InlineData("Required")]
    [InlineData("Command")]
    [InlineData("ConnectionTimeoutSeconds")]
    [InlineData("ToolCallTimeoutSeconds")]
    public void AppHost_Should_forward_scalar_mcp_configuration_to_api(string optionName)
    {
        var source = ReadAppHostSource();

        source.Should().Contain($"builder.Configuration[\"Mcp:CnvRegulation:{optionName}\"]");
        source.Should().Contain($"\"Mcp__CnvRegulation__{optionName}\"");
    }

    [Fact]
    public void AppHost_Should_forward_configured_args_exactly_and_default_to_postgres_storage()
    {
        var source = ReadAppHostSource();

        source.Should().Contain("GetSection(\"Mcp:CnvRegulation:Args\")");
        source.Should().Contain("Value: section.Value ?? string.Empty");
        source.Should().Contain("$\"Mcp__CnvRegulation__Args__{argument.Index}\"");
        source.Should().NotContain(
            ".Where(value => !string.IsNullOrWhiteSpace(value))");
        source.Should().Contain("\"--storage\"");
        source.Should().Contain("\"postgres\"");
    }

    private static string ReadAppHostSource()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "Orchestration.AppHost",
                "AppHost.cs");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate Orchestration.AppHost/AppHost.cs.");
    }
}
