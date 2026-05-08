using CnvRegulation.Application.Contracts;
using CnvRegulation.Infrastructure.Chunking;
using CnvRegulation.Infrastructure.InMemory;
using CnvRegulation.Infrastructure.Repositories;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class InMemoryRegulationArticleServiceTests
{
    [Fact]
    public async Task GetArticleAsync_ShouldReturnStructuredArticleResponse()
    {
        var repository = new InMemoryRegulationRepository();
        var service = new InMemoryRegulationArticleService(repository, repository);
        var request = new GetRegulationArticleRequest
        {
            Title = "Titulo VII",
            Chapter = "Capitulo II",
            Article = "Articulo 4"
        };

        var response = await service.GetArticleAsync(request, CancellationToken.None);

        response.Text.Should().Contain("Mock regulatory text");
        response.Citation.Article.Should().Be("Articulo 4");
        response.Citation.Title.Should().Be("Titulo VII");
        response.Citation.Url.Should().NotBeNullOrWhiteSpace();
        response.Confidence.Should().BeGreaterThan(0);
        response.Warnings.Should().Contain(warning => warning.Contains("Mock data", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetArticleAsync_ShouldReturnIngestedChunk_WhenArticleExists()
    {
        var repository = new InMemoryRegulationRepository();
        var document = LegalStructureRegulationChunkerTests.CreateDocument();
        await repository.SaveAsync(document, CancellationToken.None);
        var chunks = await new LegalStructureRegulationChunker(new LegalStructureDetector())
            .ChunkAsync(document, CancellationToken.None);
        await repository.ReplaceForDocumentAsync(document.Id, chunks, CancellationToken.None);
        var service = new InMemoryRegulationArticleService(repository, repository);

        var response = await service.GetArticleAsync(
            new GetRegulationArticleRequest
            {
                Article = "Artículo 1"
            },
            CancellationToken.None);

        response.Text.Should().Contain("primer artículo de prueba");
        response.Citation.Title.Should().Be("Normas CNV N.T. 2013 - Sample");
        response.Citation.Chapter.Should().Be("Capítulo I");
        response.Citation.Article.Should().Be("Artículo 1");
        response.Confidence.Should().BeGreaterThan(0.9);
    }

    [Fact]
    public async Task GetArticleAsync_ShouldFallbackToMock_WhenArticleDoesNotExist()
    {
        var repository = new InMemoryRegulationRepository();
        var document = LegalStructureRegulationChunkerTests.CreateDocument();
        await repository.SaveAsync(document, CancellationToken.None);
        var chunks = await new LegalStructureRegulationChunker(new LegalStructureDetector())
            .ChunkAsync(document, CancellationToken.None);
        await repository.ReplaceForDocumentAsync(document.Id, chunks, CancellationToken.None);
        var service = new InMemoryRegulationArticleService(repository, repository);

        var response = await service.GetArticleAsync(
            new GetRegulationArticleRequest
            {
                Article = "Artículo 99"
            },
            CancellationToken.None);

        response.Text.Should().Contain("Mock regulatory text for Artículo 99");
        response.Citation.Article.Should().Be("Artículo 99");
        response.Confidence.Should().Be(0.56);
    }
}
