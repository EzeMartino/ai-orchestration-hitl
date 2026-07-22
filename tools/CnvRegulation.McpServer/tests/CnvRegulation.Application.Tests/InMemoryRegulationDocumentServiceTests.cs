using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.InMemory;
using CnvRegulation.Infrastructure.Repositories;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class InMemoryRegulationDocumentServiceTests
{
    [Fact]
    public async Task GetDocumentAsync_ShouldReturnFoundDocumentMetadataContentAndCitations()
    {
        var repository = new InMemoryRegulationRepository();
        var document = LegalStructureRegulationChunkerTests.CreateDocument();
        await repository.SaveAsync(document, CancellationToken.None);
        var service = new InMemoryRegulationDocumentService(repository);
        var request = new GetRegulationDocumentRequest
        {
            DocumentId = document.Id
        };

        var response = await service.GetDocumentAsync(request, CancellationToken.None);

        response.Found.Should().BeTrue();
        response.Document.Should().NotBeNull();
        response.Document!.Id.Should().Be(document.Id);
        response.Document.Title.Should().NotBeNullOrWhiteSpace();
        response.Document.Text.Should().Contain("primer artículo de prueba");
        response.Citations.Should().NotBeEmpty();
        response.Citations[0].Url.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetDocumentAsync_ShouldReturnExplicitMissingResponse_WhenDocumentDoesNotExist()
    {
        var service = new InMemoryRegulationDocumentService(new InMemoryRegulationRepository());

        var response = await service.GetDocumentAsync(
            new GetRegulationDocumentRequest { DocumentId = "missing-document" },
            CancellationToken.None);

        response.Found.Should().BeFalse();
        response.Document.Should().BeNull();
        response.Citations.Should().BeEmpty();
        response.Warnings.Should().ContainSingle()
            .Which.Should().Contain("No se encontró");
    }

    [Fact]
    public async Task GetDocumentAsync_ShouldNotLabelNonMockRepositoryDocumentAsMock()
    {
        var repository = new InMemoryRegulationRepository();
        var document = new RegulationDocument
        {
            Id = "ingested-document",
            Source = "CNV",
            DocumentType = "Resolución General",
            Title = "Documento ingerido",
            Url = "https://www.cnv.gov.ar/documento",
            Status = "published",
            Text = "Contenido regulatorio ingerido."
        };
        await repository.SaveAsync(document, CancellationToken.None);
        var service = new InMemoryRegulationDocumentService(repository);

        var response = await service.GetDocumentAsync(
            new GetRegulationDocumentRequest { DocumentId = document.Id },
            CancellationToken.None);

        response.Found.Should().BeTrue();
        response.Document.Should().BeSameAs(document);
        response.Warnings.Should().BeEmpty();
    }
}
