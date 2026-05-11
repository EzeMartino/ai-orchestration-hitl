using CnvRegulation.Application.Contracts;
using CnvRegulation.Infrastructure.InMemory;
using CnvRegulation.Infrastructure.Repositories;
using CnvRegulation.Infrastructure.Search;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class InMemoryRegulationSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_ShouldReturnMockResults_WhenQueryIsValid()
    {
        var repository = new InMemoryRegulationRepository();
        var service = new InMemoryRegulationSearchService(repository, repository, CreateQueryExpander());
        var request = new SearchRegulationRequest
        {
            Query = "obligaciones de agentes ALyC",
            Area = "Agentes",
            Limit = 5
        };

        var response = await service.SearchAsync(request, CancellationToken.None);

        response.Query.Should().Be("obligaciones de agentes ALyC");
        response.Results.Should().NotBeEmpty();
        response.Warnings.Should().Contain(warning => warning.Contains("Mock data", StringComparison.OrdinalIgnoreCase));

        var result = response.Results[0];
        result.DocumentId.Should().NotBeNullOrWhiteSpace();
        result.Source.Should().NotBeNullOrWhiteSpace();
        result.Url.Should().NotBeNullOrWhiteSpace();
        result.Citations.Should().NotBeEmpty();
    }

    [Fact]
    public async Task InMemorySearch_ShouldStillWork()
    {
        var repository = new InMemoryRegulationRepository();
        await repository.SaveAsync(
            new()
            {
                Id = "alyc-test",
                Source = "CNV",
                DocumentType = "Texto Ordenado",
                Title = "Agentes",
                Url = "https://example.test",
                Status = "candidate",
                Text = "Obligaciones del agente de liquidación y compensación."
            },
            CancellationToken.None);
        var service = new InMemoryRegulationSearchService(repository, repository, CreateQueryExpander());

        var response = await service.SearchAsync(
            new SearchRegulationRequest { Query = "ALyC obligaciones", Limit = 5 },
            CancellationToken.None);

        response.Query.Should().Be("ALyC obligaciones");
        response.Results.Should().Contain(result => result.DocumentId == "alyc-test");
        response.Warnings.Should().Contain(warning => warning.Contains("Query expansion applied", StringComparison.OrdinalIgnoreCase));
    }

    private static StaticRegulationQueryExpander CreateQueryExpander() =>
        new(new RegulationAliasesOptions());
}
