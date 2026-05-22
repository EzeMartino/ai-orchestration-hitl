using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orchestration.Infrastructure.Agents.Legal.Regulations;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;
using Xunit;

namespace Orchestration.Tests.Agents.Legal;

public class CnvRegulationStdioMcpClientTests
{
    [Fact]
    public async Task SearchAsync_Should_throw_when_args_are_missing()
    {
        var options = Options.Create(new CnvRegulationMcpOptions
        {
            Enabled = true,
            Command = "dotnet",
            Args = [] // Empty args
        });

        var client = new CnvRegulationStdioMcpClient(
            options,
            NullLogger<CnvRegulationStdioMcpClient>.Instance
        );

        var request = new CnvRegulationSearchRequest(Query: "test", Area: "Agentes", Limit: 5, RequiresReview: true);

        var act = async () => await client.SearchAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*args are missing*");

        client.IsConnected.Should().BeFalse();
        client.ColdStartCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_Should_track_failures_and_errors_on_invalid_command()
    {
        var options = Options.Create(new CnvRegulationMcpOptions
        {
            Enabled = true,
            Command = "non-existent-executable-file-name",
            Args = ["some-arg"],
            ConnectionTimeoutSeconds = 1,
            ToolCallTimeoutSeconds = 1
        });

        var client = new CnvRegulationStdioMcpClient(
            options,
            NullLogger<CnvRegulationStdioMcpClient>.Instance
        );

        var request = new CnvRegulationSearchRequest(Query: "test", Area: "Agentes", Limit: 5, RequiresReview: true);

        var act = async () => await client.SearchAsync(request, CancellationToken.None);

        // Attempt 1: Should fail to connect because the command doesn't exist
        await act.Should().ThrowAsync<Exception>();

        client.IsConnected.Should().BeFalse();
        client.LastError.Should().NotBeNullOrEmpty();
        client.ColdStartCount.Should().Be(0); // Failed cold starts do not increment successful connection counts
        
        // Let's dispose it and ensure no crash
        await client.DisposeAsync();
    }
}
