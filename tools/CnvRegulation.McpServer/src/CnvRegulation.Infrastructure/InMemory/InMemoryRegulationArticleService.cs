using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Infrastructure.InMemory;

/// <summary>
/// Mock in-memory article implementation for the first CNV MCP MVP.
/// </summary>
public sealed class InMemoryRegulationArticleService : IRegulationArticleService
{
    /// <inheritdoc />
    public Task<GetRegulationArticleResponse> GetArticleAsync(
        GetRegulationArticleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var article = string.IsNullOrWhiteSpace(request.Article)
            ? "Articulo mock"
            : request.Article.Trim();
        var title = string.IsNullOrWhiteSpace(request.Title)
            ? "Normas CNV N.T. 2013"
            : request.Title.Trim();
        var text = $"Mock regulatory text for {article}. This placeholder does not represent official CNV text.";

        return Task.FromResult(new GetRegulationArticleResponse
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
        });
    }
}
