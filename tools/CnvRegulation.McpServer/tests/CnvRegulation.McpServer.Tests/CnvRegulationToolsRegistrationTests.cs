using System.Reflection;
using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Infrastructure.InMemory;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace CnvRegulation.McpServer.Tests;

public sealed class CnvRegulationToolsRegistrationTests
{
    [Fact]
    public void CnvRegulationTools_ShouldExposeExpectedMcpToolNames()
    {
        var toolNames = typeof(CnvRegulationTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.Name)
            .ToArray();

        toolNames.Should().BeEquivalentTo(
        [
            "search_cnv_regulation",
            "get_cnv_document",
            "get_cnv_article",
            "get_recent_cnv_resolutions",
            "analyze_text_against_cnv"
        ]);
    }

    [Fact]
    public void AddCnvRegulationMcpServices_ShouldRegisterApplicationInterfaces()
    {
        var services = new ServiceCollection();

        using var provider = services
            .AddCnvRegulationMcpServices()
            .BuildServiceProvider();

        provider.GetRequiredService<IRegulationSearchService>().Should().BeOfType<InMemoryRegulationSearchService>();
        provider.GetRequiredService<IRegulationDocumentService>().Should().BeOfType<InMemoryRegulationDocumentService>();
        provider.GetRequiredService<IRegulationArticleService>().Should().BeOfType<InMemoryRegulationArticleService>();
        provider.GetRequiredService<IRecentResolutionService>().Should().BeOfType<InMemoryRecentResolutionService>();
        provider.GetRequiredService<IComplianceAnalysisService>().Should().BeOfType<MockComplianceAnalysisService>();
    }

    [Fact]
    public async Task SearchCnvRegulationAsync_ShouldUseApplicationService()
    {
        var service = new InMemoryRegulationSearchService();

        var response = await CnvRegulationTools.SearchCnvRegulationAsync(
            service,
            "obligaciones de agentes ALyC",
            "Agentes",
            5,
            CancellationToken.None);

        response.Should().BeOfType<SearchRegulationResponse>();
        response.Results.Should().NotBeEmpty();
        response.Warnings.Should().Contain(warning => warning.Contains("Mock data", StringComparison.OrdinalIgnoreCase));
    }
}
