using CnvRegulation.Application.Contracts;
using CnvRegulation.Infrastructure.InMemory;
using CnvRegulation.Infrastructure.Repositories;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class InMemoryRegulationSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_ShouldReturnMockResults_WhenQueryIsValid()
    {
        var repository = new InMemoryRegulationRepository();
        var service = new InMemoryRegulationSearchService(repository, repository);
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
}
