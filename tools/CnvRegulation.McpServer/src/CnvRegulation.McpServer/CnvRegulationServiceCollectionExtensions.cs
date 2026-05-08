using CnvRegulation.Application.Abstractions;
using CnvRegulation.Infrastructure.Chunking;
using CnvRegulation.Infrastructure.Ingestion;
using CnvRegulation.Infrastructure.InMemory;
using CnvRegulation.Infrastructure.Repositories;
using CnvRegulation.Infrastructure.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace CnvRegulation.McpServer;

/// <summary>
/// Registers CNV regulation services used by the MCP server.
/// </summary>
public static class CnvRegulationServiceCollectionExtensions
{
    /// <summary>
    /// Adds mock CNV regulation application services.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddCnvRegulationMcpServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<InMemoryRegulationRepository>();
        services.AddSingleton<IRegulationRepository>(provider => provider.GetRequiredService<InMemoryRegulationRepository>());
        services.AddSingleton<IRegulationChunkRepository>(provider => provider.GetRequiredService<InMemoryRegulationRepository>());
        services.AddSingleton<LegalStructureDetector>();
        services.AddSingleton<IRegulationChunker, CnvRegulationChunker>();
        services.AddSingleton<PlainTextRegulationParser>();
        services.AddSingleton<HtmlRegulationParser>();
        services.AddSingleton<SidecarMetadataReader>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new HttpClient());
        services.AddSingleton<ISourceDiscoveryService, CuratedSourceDiscoveryService>();
        services.AddSingleton<ISourceDownloadService, ManifestSourceDownloadService>();
        services.AddSingleton<IRegulationIngestionService, LocalRegulationIngestionService>();
        services.AddSingleton<IRegulationSearchService, InMemoryRegulationSearchService>();
        services.AddSingleton<IRegulationDocumentService, InMemoryRegulationDocumentService>();
        services.AddSingleton<IRegulationArticleService, InMemoryRegulationArticleService>();
        services.AddSingleton<IRecentResolutionService, InMemoryRecentResolutionService>();
        services.AddSingleton<IComplianceAnalysisService, MockComplianceAnalysisService>();

        return services;
    }
}
