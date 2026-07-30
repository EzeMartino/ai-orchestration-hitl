namespace Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

public interface ICnvRegulationMcpClient
{
    Task<CnvRegulationSearchResponse> SearchAsync(
        CnvRegulationSearchRequest request,
        CancellationToken cancellationToken
    );

    Task<CnvRegulationDocumentResponse> GetDocumentAsync(
        CnvRegulationDocumentRequest request,
        CancellationToken cancellationToken
    );

    Task<CnvRegulationArticleResponse> GetArticleAsync(
        CnvRegulationArticleRequest request,
        CancellationToken cancellationToken
    );

    bool IsConnected { get; }
    int ColdStartCount { get; }
    int ResetCount { get; }
    string? LastError { get; }
}
