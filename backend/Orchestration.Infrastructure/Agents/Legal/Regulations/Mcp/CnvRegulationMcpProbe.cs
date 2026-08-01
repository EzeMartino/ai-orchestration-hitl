namespace Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

public sealed class CnvRegulationMcpProbe(ICnvRegulationMcpClient client) : ICnvRegulationMcpProbe
{
    private const string ReadinessQuery = "CNV";

    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        var response = await client.SearchAsync(
            new CnvRegulationSearchRequest(ReadinessQuery, Limit: 1),
            cancellationToken);

        if (CnvRegulationMcpResponseContract.IsInvalidStructuredContent(
                response,
                ReadinessQuery))
        {
            throw new InvalidOperationException(
                "CNV MCP readiness probe received an invalid response.");
        }
    }
}
