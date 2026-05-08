using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Infrastructure.InMemory;

/// <summary>
/// Mock in-memory document implementation for the first CNV MCP MVP.
/// </summary>
public sealed class InMemoryRegulationDocumentService : IRegulationDocumentService
{
    /// <inheritdoc />
    public Task<GetRegulationDocumentResponse> GetDocumentAsync(
        GetRegulationDocumentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var documentId = string.IsNullOrWhiteSpace(request.DocumentId)
            ? "unknown"
            : request.DocumentId.Trim();

        var document = MockRegulationData.GetDocumentOrDefault(documentId);

        return Task.FromResult(new GetRegulationDocumentResponse
        {
            Document = document,
            Citations = [MockRegulationData.CreateDocumentCitation(document)],
            Warnings = [MockRegulationData.MockWarning]
        });
    }
}
