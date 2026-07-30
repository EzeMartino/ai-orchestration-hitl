using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Infrastructure.Chunking;

namespace CnvRegulation.Infrastructure.InMemory;

/// <summary>
/// In-memory article implementation for repository-backed CNV chunks.
/// </summary>
public sealed class InMemoryRegulationArticleService(
    IRegulationChunkRepository chunkRepository,
    IRegulationRepository repository) : IRegulationArticleService
{
    /// <inheritdoc />
    public async Task<GetRegulationArticleResponse> GetArticleAsync(
        GetRegulationArticleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var requestedArticleKey = LegalStructureDetector.NormalizeArticleKey(request.Article);
        var chunks = await chunkRepository.ListChunksAsync(cancellationToken).ConfigureAwait(false);
        foreach (var chunk in chunks.OrderBy(chunk => chunk.ChunkIndex))
        {
            if (!MatchesRequest(chunk, requestedArticleKey, request))
            {
                continue;
            }

            var document = await repository.GetByIdAsync(chunk.DocumentId, cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                continue;
            }

            return new GetRegulationArticleResponse
            {
                Found = true,
                Text = chunk.Text,
                Citation = new CnvRegulation.Domain.RegulationCitation
                {
                    Source = document.Source,
                    DocumentType = document.DocumentType,
                    ResolutionNumber = document.ResolutionNumber,
                    Title = document.Title,
                    Chapter = chunk.Chapter,
                    Section = chunk.Section,
                    Article = chunk.Article,
                    PublicationDate = document.PublicationDate,
                    Url = document.Url,
                    QuotedText = chunk.Text
                },
                Confidence = 0.92,
                Warnings = CreateSourceWarnings(document)
            };
        }

        return new GetRegulationArticleResponse
        {
            Found = false,
            Text = null,
            Citation = null,
            Confidence = 0,
            Warnings = [$"No se encontró el artículo regulatorio '{request.Article?.Trim()}'."]
        };
    }

    private static bool MatchesRequest(
        CnvRegulation.Domain.RegulationChunk chunk,
        string requestedArticleKey,
        GetRegulationArticleRequest request)
    {
        var chunkArticleKey = LegalStructureDetector.NormalizeArticleKey(chunk.Article ?? string.Empty);
        if (!string.Equals(chunkArticleKey, requestedArticleKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return MatchesOptional(chunk.Title, request.Title)
            && MatchesOptional(chunk.Chapter, request.Chapter)
            && MatchesOptional(chunk.Section, request.Section);
    }

    private static bool MatchesOptional(string? actual, string? expected)
    {
        return string.IsNullOrWhiteSpace(expected)
            || string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> CreateSourceWarnings(CnvRegulation.Domain.RegulationDocument document)
    {
        var warnings = new List<string>();

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
