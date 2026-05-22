namespace Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

public interface ICnvRegulationMcpClient
{
    Task<CnvRegulationSearchResponse> SearchAsync(
        CnvRegulationSearchRequest request,
        CancellationToken cancellationToken
    );

    bool IsConnected { get; }
    int ColdStartCount { get; }
    int ResetCount { get; }
    string? LastError { get; }
}