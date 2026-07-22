using CnvRegulation.Application.Contracts;
using CnvRegulation.Infrastructure.Chunking;
using CnvRegulation.Infrastructure.InMemory;
using CnvRegulation.Infrastructure.Repositories;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class InMemoryRegulationArticleServiceTests
{
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

        response.Found.Should().BeTrue();
        response.Text.Should().NotBeNull();
        response.Text.Should().Contain("primer artículo de prueba");
        response.Citation.Should().NotBeNull();
        response.Citation.Title.Should().Be("Normas CNV N.T. 2013 - Sample");
        response.Citation.Chapter.Should().Be("Capítulo I");
        response.Citation.Article.Should().Be("Artículo 1");
        response.Confidence.Should().BeGreaterThan(0.9);
    }

    [Fact]
    public async Task GetArticleAsync_ShouldReturnExplicitMissingResponse_WhenArticleDoesNotExist()
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

        response.Found.Should().BeFalse();
        response.Text.Should().BeNull();
        response.Citation.Should().BeNull();
        response.Confidence.Should().Be(0);
        response.Warnings.Should().ContainSingle()
            .Which.Should().Contain("No se encontró");
        response.Text.Should().NotContain("Mock regulatory text");
    }
}
