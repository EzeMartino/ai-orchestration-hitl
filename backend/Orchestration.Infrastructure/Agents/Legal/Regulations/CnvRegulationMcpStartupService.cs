using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

namespace Orchestration.Infrastructure.Agents.Legal.Regulations;

public sealed class CnvRegulationMcpStartupService(
    IOptions<CnvRegulationMcpOptions> options,
    ICnvRegulationMcpProbe probe) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Required)
        {
            return;
        }

        try
        {
            await probe.ProbeAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("CNV MCP readiness probe failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
