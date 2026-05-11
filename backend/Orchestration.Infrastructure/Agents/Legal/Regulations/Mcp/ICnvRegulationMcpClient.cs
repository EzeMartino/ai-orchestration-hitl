namespace Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

public interface ICnvRegulationMcpClient
{
    Task<CnvRegulationSearchResponse> SearchAsync(
        CnvRegulationSearchRequest request,
        CancellationToken cancellationToken
    );
}