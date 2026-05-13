using System.Reflection;
using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Analysis;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Infrastructure.InMemory;
using CnvRegulation.Infrastructure.Persistence;
using CnvRegulation.Infrastructure.Repositories;
using CnvRegulation.Infrastructure.Search;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
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

        provider.GetRequiredService<IRegulationRepository>().Should().BeOfType<InMemoryRegulationRepository>();
        provider.GetRequiredService<IRegulationChunkRepository>().Should().BeOfType<InMemoryRegulationRepository>();
        provider.GetRequiredService<IRegulationEmbeddingRepository>().Should().BeOfType<InMemoryRegulationRepository>();
        provider.GetRequiredService<IRegulationChunker>().Should().NotBeNull();
        provider.GetRequiredService<IRegulationEmbeddingService>().Should().NotBeNull();
        provider.GetRequiredService<IEmbeddingGenerator>().Should().NotBeNull();
        provider.GetRequiredService<IRegulationChunkHasher>().Should().NotBeNull();
        provider.GetRequiredService<IRegulationIngestionService>().Should().NotBeNull();
        provider.GetRequiredService<ISourceDiscoveryService>().Should().NotBeNull();
        provider.GetRequiredService<ISourceDownloadService>().Should().NotBeNull();
        provider.GetRequiredService<ISourceInspectionService>().Should().NotBeNull();
        provider.GetRequiredService<IInfolegLinkDiscoveryService>().Should().NotBeNull();
        provider.GetRequiredService<IRegulationCoverageInspectionService>().Should().NotBeNull();
        provider.GetRequiredService<ISearchQualityValidationService>().Should().NotBeNull();
        provider.GetRequiredService<IExplainSearchQueryService>().Should().NotBeNull();
        provider.GetRequiredService<IRegulationQueryExpander>().Should().BeOfType<StaticRegulationQueryExpander>();
        provider.GetRequiredService<IRegulationSearchService>().Should().BeOfType<InMemoryRegulationSearchService>();
        provider.GetRequiredService<IRegulationDocumentService>().Should().BeOfType<InMemoryRegulationDocumentService>();
        provider.GetRequiredService<IRegulationArticleService>().Should().BeOfType<InMemoryRegulationArticleService>();
        provider.GetRequiredService<IRecentResolutionService>().Should().BeOfType<InMemoryRecentResolutionService>();
        provider.GetRequiredService<IRegulatoryTopicExtractor>().Should().BeOfType<RegulatoryTopicExtractor>();
        provider.GetRequiredService<IRegulatoryFindingBuilder>().Should().BeOfType<RegulatoryFindingBuilder>();
        provider.GetRequiredService<IComplianceAnalysisService>().Should().BeOfType<CnvTextAnalysisService>();
    }

    [Fact]
    public void AddCnvRegulationMcpServices_ShouldRegisterPostgresRepository_WhenConfigured()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RegulationDb:Provider"] = "Postgres",
                ["RegulationDb:ConnectionString"] = "Host=localhost;Database=cnv_regulation;Username=postgres;Password=postgres"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddCnvRegulationMcpServices(configuration, storageProvider: null);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IRegulationRepository>().Should().BeOfType<PostgresRegulationRepository>();
        provider.GetRequiredService<IRegulationChunkRepository>().Should().BeOfType<PostgresRegulationRepository>();
        provider.GetRequiredService<IRegulationEmbeddingRepository>().Should().BeOfType<PostgresRegulationRepository>();
        provider.GetRequiredService<IRegulationSearchService>().Should().BeOfType<PostgresRegulationSearchService>();
        provider.GetRequiredService<IRegulationDatabaseMigrator>().Should().BeOfType<PostgresRegulationDatabaseMigrator>();
    }

    [Fact]
    public async Task SearchCnvRegulationAsync_ShouldUseApplicationService()
    {
        var repository = new InMemoryRegulationRepository();
        var service = new InMemoryRegulationSearchService(repository, repository, CreateQueryExpander());

        var response = await CnvRegulationTools.SearchCnvRegulationAsync(
            service,
            "obligaciones de agentes ALyC",
            "Agentes",
            5,
            cancellationToken: CancellationToken.None);

        response.Should().BeOfType<SearchRegulationResponse>();
        response.Results.Should().NotBeEmpty();
        response.Warnings.Should().Contain(warning => warning.Contains("Mock data", StringComparison.OrdinalIgnoreCase));
    }

    private static StaticRegulationQueryExpander CreateQueryExpander() =>
        new(new RegulationAliasesOptions());
}
