namespace Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

public interface ICnvRegulationMcpProbe
{
    Task ProbeAsync(CancellationToken cancellationToken);
}
