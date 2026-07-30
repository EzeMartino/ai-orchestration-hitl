namespace Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

public sealed class CnvRegulationMcpProbe(ICnvRegulationMcpClient client) : ICnvRegulationMcpProbe
{
    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        await client.SearchAsync(
            new CnvRegulationSearchRequest("CNV", Limit: 1),
            cancellationToken);
    }
}
