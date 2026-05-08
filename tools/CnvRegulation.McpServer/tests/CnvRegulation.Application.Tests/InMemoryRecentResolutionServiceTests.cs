using CnvRegulation.Application.Contracts;
using CnvRegulation.Infrastructure.InMemory;
using FluentAssertions;

namespace CnvRegulation.Application.Tests;

public sealed class InMemoryRecentResolutionServiceTests
{
    [Fact]
    public async Task GetRecentAsync_ShouldReturnRecentMockItems_WhenSourceMatches()
    {
        var service = new InMemoryRecentResolutionService();
        var request = new GetRecentResolutionsRequest
        {
            Days = 30,
            Source = "boletin_oficial"
        };

        var response = await service.GetRecentAsync(request, CancellationToken.None);

        response.Results.Should().NotBeEmpty();
        response.Results.Should().OnlyContain(item => item.Source == "boletin_oficial");
        response.Results.Should().OnlyContain(item => item.PublicationDate.HasValue);
        response.Results.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.Source));
        response.Warnings.Should().Contain(warning => warning.Contains("Mock data", StringComparison.OrdinalIgnoreCase));
    }
}
