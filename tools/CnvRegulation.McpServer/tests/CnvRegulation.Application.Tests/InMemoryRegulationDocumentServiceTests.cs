using CnvRegulation.Application.Contracts;
using CnvRegulation.Infrastructure.InMemory;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class InMemoryRegulationDocumentServiceTests
{
    [Fact]
    public async Task GetDocumentAsync_ShouldReturnDocumentMetadataContentAndCitations()
    {
        var service = new InMemoryRegulationDocumentService();
        var request = new GetRegulationDocumentRequest
        {
            DocumentId = "cnv-nt-2013"
        };

        var response = await service.GetDocumentAsync(request, CancellationToken.None);

        response.Document.Id.Should().Be("cnv-nt-2013");
        response.Document.Title.Should().NotBeNullOrWhiteSpace();
        response.Document.Text.Should().Contain("Mock regulatory text");
        response.Citations.Should().NotBeEmpty();
        response.Citations[0].Url.Should().NotBeNullOrWhiteSpace();
        response.Warnings.Should().Contain(warning => warning.Contains("Mock data", StringComparison.OrdinalIgnoreCase));
    }
}
