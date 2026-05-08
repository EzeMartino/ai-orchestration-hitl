using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Infrastructure.Chunking;

namespace CnvRegulation.Infrastructure.InMemory;

/// <summary>
/// In-memory article implementation for locally ingested chunks and mock fallback data.
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
                Warnings = [MockRegulationData.MockWarning]
            };
        }

        var article = string.IsNullOrWhiteSpace(request.Article)
            ? "Articulo mock"
            : request.Article.Trim();
        var title = string.IsNullOrWhiteSpace(request.Title)
            ? "Normas CNV N.T. 2013"
            : request.Title.Trim();
        var text = $"Mock regulatory text for {article}. This placeholder does not represent official CNV text.";

        return new GetRegulationArticleResponse
        {
            Text = text,
            Citation = MockRegulationData.CreateCitation(
                title,
                "Normas CNV",
                null,
                request.Chapter,
                request.Section,
                article,
                text),
            Confidence = 0.56,
            Warnings = [MockRegulationData.MockWarning]
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
}
