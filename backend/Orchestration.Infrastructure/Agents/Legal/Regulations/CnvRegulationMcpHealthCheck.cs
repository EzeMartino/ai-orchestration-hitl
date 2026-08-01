using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

namespace Orchestration.Infrastructure.Agents.Legal.Regulations;

public sealed class CnvRegulationMcpHealthCheck(
    IOptions<CnvRegulationMcpOptions> options,
    ICnvRegulationMcpProbe probe) : IHealthCheck
{
    private const string FailedDescription = "CNV MCP readiness probe failed.";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
        {
            return HealthCheckResult.Healthy("CNV MCP is optional and disabled.");
        }

        try
        {
            await probe.ProbeAsync(cancellationToken);
            return HealthCheckResult.Healthy("CNV MCP readiness probe completed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy(FailedDescription);
        }
    }
}
