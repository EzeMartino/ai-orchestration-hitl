using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Infrastructure.InMemory;

/// <summary>
/// In-memory document implementation for repository-backed CNV documents.
/// </summary>
public sealed class InMemoryRegulationDocumentService(IRegulationRepository repository) : IRegulationDocumentService
{
    /// <inheritdoc />
    public async Task<GetRegulationDocumentResponse> GetDocumentAsync(
        GetRegulationDocumentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var documentId = string.IsNullOrWhiteSpace(request.DocumentId)
            ? "unknown"
            : request.DocumentId.Trim();

        var document = await repository.GetByIdAsync(documentId, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return new GetRegulationDocumentResponse
            {
                Found = false,
                Document = null,
                Citations = [],
                Warnings = [$"No se encontró el documento regulatorio '{documentId}'."]
            };
        }

        return new GetRegulationDocumentResponse
        {
            Found = true,
            Document = document,
            Citations = [MockRegulationData.CreateDocumentCitation(document)],
            Warnings = CreateSourceWarnings(document)
        };
    }

    private static IReadOnlyList<string> CreateSourceWarnings(CnvRegulation.Domain.RegulationDocument document)
    {
        var warnings = new List<string>();

        if (string.Equals(document.Status, "mock", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(MockRegulationData.MockWarning);
        }

        if (string.Equals(document.Status, "candidate", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("candidate source");
        }

        if (document.RequiresReview)
        {
            warnings.Add("requires review");
        }

        return warnings.Count == 0 ? [] : warnings;
    }
}
