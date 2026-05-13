using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Analysis;
using CnvRegulation.Infrastructure.Chunking;
using CnvRegulation.Infrastructure.Deduplication;
using CnvRegulation.Infrastructure.Diagnostics;
using CnvRegulation.Infrastructure.Embeddings;
using CnvRegulation.Infrastructure.Ingestion;
using CnvRegulation.Infrastructure.InMemory;
using CnvRegulation.Infrastructure.Persistence;
using CnvRegulation.Infrastructure.Parsing;
using CnvRegulation.Infrastructure.Repositories;
using CnvRegulation.Infrastructure.Sources;
using CnvRegulation.Infrastructure.Search;
using Microsoft.Extensions.Configuration;
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
        return services.AddCnvRegulationMcpServices(configuration: null, storageProvider: "InMemory");
    }

    /// <summary>
    /// Adds CNV regulation application services with optional storage configuration.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="storageProvider">An explicit storage provider override.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddCnvRegulationMcpServices(
        this IServiceCollection services,
        IConfiguration? configuration,
        string? storageProvider)
    {
        ArgumentNullException.ThrowIfNull(services);

        var dbOptions = RegulationDbOptions.Create(
            storageProvider ?? configuration?["RegulationDb:Provider"],
            configuration?["RegulationDb:ConnectionString"]);
        var embeddingOptions = EmbeddingOptions.Create(
            configuration?["Embeddings:Enabled"],
            configuration?["Embeddings:Provider"],
            configuration?["Embeddings:Model"],
            configuration?["Embeddings:Dimensions"],
            configuration?["Embeddings:ApiKey"],
            configuration?["Embeddings:Endpoint"]);

        services.AddSingleton(dbOptions);
        services.AddSingleton(embeddingOptions);
        if (ShouldUsePostgres(dbOptions, storageProvider))
        {
            services.AddSingleton<RegulationDbConnectionFactory>();
            services.AddSingleton<IRegulationDatabaseMigrator, PostgresRegulationDatabaseMigrator>();
            services.AddSingleton<PostgresRegulationRepository>();
            services.AddSingleton<IRegulationRepository>(provider =>
                provider.GetRequiredService<PostgresRegulationRepository>());
            services.AddSingleton<IRegulationChunkRepository>(provider =>
                provider.GetRequiredService<PostgresRegulationRepository>());
            services.AddSingleton<IRegulationEmbeddingRepository>(provider =>
                provider.GetRequiredService<PostgresRegulationRepository>());
            services.AddSingleton<IRegulationSearchService, PostgresRegulationSearchService>();
        }
        else
        {
            services.AddSingleton<InMemoryRegulationRepository>();
            services.AddSingleton<IRegulationRepository>(provider =>
                provider.GetRequiredService<InMemoryRegulationRepository>());
            services.AddSingleton<IRegulationChunkRepository>(provider =>
                provider.GetRequiredService<InMemoryRegulationRepository>());
            services.AddSingleton<IRegulationEmbeddingRepository>(provider =>
                provider.GetRequiredService<InMemoryRegulationRepository>());
            services.AddSingleton<IRegulationSearchService, InMemoryRegulationSearchService>();
        }

        services.AddSingleton<LegalStructureDetector>();
        services.AddSingleton<IRegulationChunker, LegalStructureRegulationChunker>();
        services.AddSingleton<IRegulationChunkHasher, RegulationChunkHasher>();
        services.AddSingleton<RegulationChunkDeduplicator>();
        services.AddSingleton<WrapperDocumentDetector>();
        services.AddSingleton<RegulationAliasesOptions>();
        services.AddSingleton<IRegulationQueryExpander, StaticRegulationQueryExpander>();
        services.AddSingleton<DeterministicFakeEmbeddingGenerator>();
        services.AddSingleton<OpenAiEmbeddingGenerator>();
        services.AddSingleton<IEmbeddingGenerator>(provider =>
            embeddingOptions.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
                ? provider.GetRequiredService<OpenAiEmbeddingGenerator>()
                : provider.GetRequiredService<DeterministicFakeEmbeddingGenerator>());
        services.AddSingleton<IRegulationEmbeddingService, RegulationEmbeddingService>();
        services.AddSingleton<PlainTextRegulationParser>();
        services.AddSingleton<HtmlRegulationParser>();
        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddSingleton<ITextNormalizer, PdfExtractedTextNormalizer>();
        services.AddSingleton<SidecarMetadataReader>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new HttpClient());
        services.AddSingleton<ISourceDiscoveryService, CuratedSourceDiscoveryService>();
        services.AddSingleton<ISourceDownloadService, ManifestSourceDownloadService>();
        services.AddSingleton<InfolegLinkExtractor>();
        services.AddSingleton<IInfolegLinkDiscoveryService, InfolegLinkDiscoveryService>();
        services.AddSingleton<ISourceInspectionService, SourceInspectionService>();
        services.AddSingleton<IChunkQualityInspectionService, ChunkQualityInspectionService>();
        services.AddSingleton<IRegulationCoverageInspectionService, RegulationCoverageInspectionService>();
        services.AddSingleton<ISearchQualityValidationService, SearchQualityValidationService>();
        services.AddSingleton<IExplainSearchQueryService, ExplainSearchQueryService>();
        services.AddSingleton<IRegulationIngestionService, LocalRegulationIngestionService>();
        services.AddSingleton<IRegulationDocumentService, InMemoryRegulationDocumentService>();
        services.AddSingleton<IRegulationArticleService, InMemoryRegulationArticleService>();
        services.AddSingleton<IRecentResolutionService, InMemoryRecentResolutionService>();
        services.AddSingleton<IRegulatoryTopicExtractor, RegulatoryTopicExtractor>();
        services.AddSingleton<IRegulatoryFindingBuilder, RegulatoryFindingBuilder>();
        services.AddSingleton<IComplianceAnalysisService, CnvTextAnalysisService>();

        return services;
    }

    private static bool ShouldUsePostgres(RegulationDbOptions dbOptions, string? storageProvider)
    {
        if (string.Equals(storageProvider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return dbOptions.UsePostgres;
    }
}
