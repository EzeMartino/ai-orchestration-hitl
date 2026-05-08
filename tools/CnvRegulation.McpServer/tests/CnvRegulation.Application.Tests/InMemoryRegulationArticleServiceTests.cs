using CnvRegulation.Application.Contracts;
using CnvRegulation.Infrastructure.InMemory;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class InMemoryRegulationArticleServiceTests
{
    [Fact]
    public async Task GetArticleAsync_ShouldReturnStructuredArticleResponse()
    {
        var service = new InMemoryRegulationArticleService();
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
}
