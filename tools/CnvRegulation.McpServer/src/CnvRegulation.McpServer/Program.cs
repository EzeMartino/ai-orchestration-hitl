using CnvRegulation.McpServer;
using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddCnvRegulationMcpServices(builder.Configuration, ResolveStorageProvider(args));

if (args.Length > 0 && string.Equals(args[0], "migrate-db", StringComparison.OrdinalIgnoreCase))
{
    using var host = builder.Build();
    var migrator = host.Services.GetRequiredService<IRegulationDatabaseMigrator>();
    await migrator.MigrateAsync(CancellationToken.None);

    Console.WriteLine("Database migration completed.");

    return;
}

if (args.Length > 0 && string.Equals(args[0], "discover-sources", StringComparison.OrdinalIgnoreCase))
{
    using var host = builder.Build();
    var manifestPath = ResolveManifestPath(args);
    var discoveryService = host.Services.GetRequiredService<ISourceDiscoveryService>();
    var response = await discoveryService.DiscoverAsync(
        new DiscoverSourcesRequest
        {
            ManifestPath = manifestPath
        },
        CancellationToken.None);

    Console.WriteLine($"Manifest: {response.ManifestPath}");
    Console.WriteLine($"Sources discovered: {response.SourcesDiscovered}");

    foreach (var warning in response.Warnings)
    {
        Console.WriteLine($"Warning: {warning}");
    }

    return;
}

if (args.Length > 0 && string.Equals(args[0], "download-sources", StringComparison.OrdinalIgnoreCase))
{
    using var host = builder.Build();
    var manifestPath = ResolveDownloadManifestPath(args);
    var outputDirectory = ResolveOutputDirectory(args);
    var downloadService = host.Services.GetRequiredService<ISourceDownloadService>();
    var response = await downloadService.DownloadAsync(
        new DownloadSourcesRequest
        {
            ManifestPath = manifestPath,
            OutputDirectory = outputDirectory
        },
        CancellationToken.None);

    Console.WriteLine($"Sources downloaded: {response.SourcesDownloaded}");
    Console.WriteLine($"Sources skipped: {response.SourcesSkipped}");

    foreach (var warning in response.Warnings)
    {
        Console.WriteLine($"Warning: {warning}");
    }

    return;
}

if (args.Length > 0 && string.Equals(args[0], "discover-infoleg-links", StringComparison.OrdinalIgnoreCase))
{
    using var host = builder.Build();
    var sourceDirectory = ResolveSourceDirectory(args);
    var outputManifestPath = ResolveInfolegDiscoveredManifestPath(args);
    var maxLinksPerSource = ResolveMaxLinksPerSource(args);
    var discoveryService = host.Services.GetRequiredService<IInfolegLinkDiscoveryService>();
    var response = await discoveryService.DiscoverAsync(
        new InfolegLinkDiscoveryRequest
        {
            SourceDirectory = sourceDirectory,
            OutputManifestPath = outputManifestPath,
            MaxLinksPerSource = maxLinksPerSource
        },
        CancellationToken.None);

    Console.WriteLine($"Infoleg files inspected: {response.InfolegFilesInspected}");
    Console.WriteLine($"Links discovered: {response.LinksDiscovered}");
    Console.WriteLine($"Links accepted: {response.LinksAccepted}");
    Console.WriteLine($"Links skipped: {response.LinksSkipped}");
    Console.WriteLine($"Manifest written: {response.ManifestPath}");

    foreach (var warning in response.Warnings)
    {
        Console.WriteLine($"Warning: {warning}");
    }

    return;
}

if (args.Length > 0 && string.Equals(args[0], "inspect-sources", StringComparison.OrdinalIgnoreCase))
{
    using var host = builder.Build();
    var sourceDirectory = ResolveSourceDirectory(args);
    var inspectionService = host.Services.GetRequiredService<ISourceInspectionService>();
    var response = await inspectionService.InspectAsync(
        new InspectSourcesRequest
        {
            SourceDirectory = sourceDirectory
        },
        CancellationToken.None);

    Console.WriteLine(SourceInspectionReportFormatter.Format(response));

    return;
}

if (args.Length > 0 && string.Equals(args[0], "inspect-chunks", StringComparison.OrdinalIgnoreCase))
{
    using var host = builder.Build();

    if (ShouldIngestBeforeChunkInspection(args))
    {
        var sourceDirectory = ResolveSourceDirectory(args);
        var ingestionService = host.Services.GetRequiredService<IRegulationIngestionService>();
        await ingestionService.IngestAsync(
            new IngestRegulationSourceRequest
            {
                SourceDirectory = sourceDirectory
            },
            CancellationToken.None);
    }

    var inspectionService = host.Services.GetRequiredService<IChunkQualityInspectionService>();
    var response = await inspectionService.InspectAsync(new InspectChunksRequest(), CancellationToken.None);

    Console.WriteLine(ChunkQualityReportFormatter.Format(response));

    return;
}

if (args.Length > 0 && string.Equals(args[0], "inspect-coverage", StringComparison.OrdinalIgnoreCase))
{
    using var host = builder.Build();
    IngestRegulationSourceResponse? ingestionResponse = null;

    if (ShouldIngestBeforeRepositoryInspection(args))
    {
        var sourceDirectory = ResolveSourceDirectory(args);
        var ingestionService = host.Services.GetRequiredService<IRegulationIngestionService>();
        ingestionResponse = await ingestionService.IngestAsync(
            new IngestRegulationSourceRequest
            {
                SourceDirectory = sourceDirectory
            },
            CancellationToken.None);
    }

    var coverageService = host.Services.GetRequiredService<IRegulationCoverageInspectionService>();
    var response = await coverageService.InspectAsync(new InspectCoverageRequest(), CancellationToken.None);

    Console.WriteLine(CoverageReportFormatter.Format(response, ingestionResponse));

    return;
}

if (args.Length > 0 && string.Equals(args[0], "validate-search-quality", StringComparison.OrdinalIgnoreCase))
{
    using var host = builder.Build();
    var querySetPath = ResolveSearchQualityQuerySetPath(args);
    var limit = ResolveSearchQualityLimit(args);
    var mode = ResolveSearchMode(args);
    var validationService = host.Services.GetRequiredService<ISearchQualityValidationService>();
    var compareModes = ResolveCompareSearchModes(args);
    if (compareModes.Count > 0)
    {
        var comparison = await validationService.CompareAsync(
            new CompareSearchQualityRequest
            {
                QuerySetPath = querySetPath,
                Limit = limit,
                Modes = compareModes
            },
            CancellationToken.None);

        Console.WriteLine(SearchQualityComparisonReportFormatter.Format(comparison));

        var outputReport = GetOptionValue(args, "--output-report");
        if (!string.IsNullOrWhiteSpace(outputReport))
        {
            var reviewReport = SearchQualityReviewReportFactory.Create(comparison, TimeProvider.System.GetUtcNow());
            var reportPath = await SearchQualityReviewReportWriter.WriteAsync(
                reviewReport,
                outputReport,
                CancellationToken.None);

            Console.WriteLine($"Review report written: {reportPath}");
        }

        return;
    }

    var response = await validationService.ValidateAsync(
        new ValidateSearchQualityRequest
        {
            QuerySetPath = querySetPath,
            Limit = limit,
            SearchMode = mode
        },
        CancellationToken.None);

    Console.WriteLine(SearchQualityValidationReportFormatter.Format(response));

    return;
}

if (args.Length > 0 && string.Equals(args[0], "validate-analysis-quality", StringComparison.OrdinalIgnoreCase))
{
    using var host = builder.Build();
    var caseSetPath = ResolveAnalysisQualityCaseSetPath(args);
    var validationService = host.Services.GetRequiredService<IAnalysisQualityValidationService>();
    var response = await validationService.ValidateAsync(
        new ValidateAnalysisQualityRequest
        {
            CaseSetPath = caseSetPath,
            UseHybridSearch = ResolveBoolOption(args, "--use-hybrid-search", defaultValue: false),
            StrictMode = ResolveBoolOption(args, "--strict-mode", defaultValue: true)
        },
        CancellationToken.None);

    Console.WriteLine(AnalysisQualityValidationReportFormatter.Format(response));

    var outputReport = GetOptionValue(args, "--output-report");
    if (!string.IsNullOrWhiteSpace(outputReport))
    {
        var reportPath = await AnalysisQualityValidationReportWriter.WriteAsync(
            response,
            outputReport,
            CancellationToken.None);

        Console.WriteLine($"Analysis quality report written: {reportPath}");
    }

    return;
}

if (args.Length > 0 && string.Equals(args[0], "generate-embeddings", StringComparison.OrdinalIgnoreCase))
{
    using var host = builder.Build();
    var embeddingService = host.Services.GetRequiredService<IRegulationEmbeddingService>();
    var response = await embeddingService.GenerateMissingEmbeddingsAsync(
        new GenerateEmbeddingsRequest
        {
            Provider = GetOptionValue(args, "--provider"),
            Model = GetOptionValue(args, "--model"),
            Dimensions = ResolveNullableIntOption(args, "--dimensions"),
            Limit = ResolveNullableIntOption(args, "--limit"),
            DryRun = HasOption(args, "--dry-run"),
            OnlyMissing = HasOption(args, "--only-missing"),
            BatchSize = ResolveNullableIntOption(args, "--batch-size") ?? 32,
            DelayMs = ResolveNullableIntOption(args, "--delay-ms") ?? 0,
            MaxInputCharacters = ResolveNullableIntOption(args, "--max-input-chars") ?? 24_000,
            FailedReportPath = GetOptionValue(args, "--failed-report"),
            IncludeDuplicates = HasOption(args, "--include-duplicates"),
            IncludeNonSearchable = HasOption(args, "--include-non-searchable")
        },
        CancellationToken.None);

    Console.WriteLine(EmbeddingGenerationReportFormatter.Format(response));

    return;
}

if (args.Length > 0 && string.Equals(args[0], "explain-search-query", StringComparison.OrdinalIgnoreCase))
{
    using var host = builder.Build();
    var request = ResolveExplainSearchQueryRequest(args);
    if (string.IsNullOrWhiteSpace(request.Query))
    {
        Console.Error.WriteLine("Query is required. Use --query \"hecho relevante\" or pass it as positional text.");

        return;
    }

    var explanationService = host.Services.GetRequiredService<IExplainSearchQueryService>();
    var response = await explanationService.ExplainAsync(request, CancellationToken.None);

    Console.WriteLine(ExplainSearchQueryReportFormatter.Format(response));

    return;
}

if (args.Length > 0 && string.Equals(args[0], "ingest", StringComparison.OrdinalIgnoreCase))
{
    using var host = builder.Build();
    var sourceDirectory = ResolveSourceDirectory(args);
    var ingestionService = host.Services.GetRequiredService<IRegulationIngestionService>();
    var response = await ingestionService.IngestAsync(
        new IngestRegulationSourceRequest
        {
            SourceDirectory = sourceDirectory
        },
        CancellationToken.None);

    Console.WriteLine($"Documents ingested: {response.DocumentsIngested}");
    Console.WriteLine($"Documents skipped: {response.DocumentsSkipped}");

    foreach (var warning in response.Warnings)
    {
        Console.WriteLine($"Warning: {warning}");
    }

    return;
}

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

static string ResolveSourceDirectory(string[] args)
{
    var switchDirectory = GetOptionValue(args, "--source-directory");
    if (!string.IsNullOrWhiteSpace(switchDirectory))
    {
        return switchDirectory;
    }

    var positionalDirectory = GetPositionalArguments(args)
        .FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(positionalDirectory))
    {
        return positionalDirectory;
    }

    return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "sources");
}

static string? ResolveStorageProvider(string[] args)
{
    if (args.Any(argument => string.Equals(argument, "--use-postgres", StringComparison.OrdinalIgnoreCase)))
    {
        return "Postgres";
    }

    if (args.Length > 0 && string.Equals(args[0], "migrate-db", StringComparison.OrdinalIgnoreCase))
    {
        return "Postgres";
    }

    return GetOptionValue(args, "--storage");
}

static bool ShouldIngestBeforeChunkInspection(string[] args)
{
    return ShouldIngestBeforeRepositoryInspection(args);
}

static bool ShouldIngestBeforeRepositoryInspection(string[] args)
{
    var storageProvider = ResolveStorageProvider(args);
    return !string.Equals(storageProvider, "Postgres", StringComparison.OrdinalIgnoreCase)
        || HasOption(args, "--source-directory");
}

static string ResolveManifestPath(string[] args)
{
    var explicitManifestPath = GetOptionValue(args, "--manifest");
    if (!string.IsNullOrWhiteSpace(explicitManifestPath))
    {
        return explicitManifestPath;
    }

    return Path.Combine(ResolveProjectRoot(), "data", "source-manifest", "sources.manifest.json");
}

static string ResolveDownloadManifestPath(string[] args)
{
    var explicitManifestPath = GetOptionValue(args, "--manifest");
    if (!string.IsNullOrWhiteSpace(explicitManifestPath))
    {
        return explicitManifestPath;
    }

    var positionalManifestPath = GetPositionalArguments(args)
        .FirstOrDefault();

    if (!string.IsNullOrWhiteSpace(positionalManifestPath))
    {
        return positionalManifestPath;
    }

    return Path.Combine(ResolveProjectRoot(), "data", "source-manifest", "sources.manifest.json");
}

static string ResolveInfolegDiscoveredManifestPath(string[] args)
{
    var explicitManifestPath = GetOptionValue(args, "--output-manifest");
    if (!string.IsNullOrWhiteSpace(explicitManifestPath))
    {
        return explicitManifestPath;
    }

    return Path.Combine(ResolveProjectRoot(), "data", "source-manifest", "infoleg.discovered.manifest.json");
}

static int ResolveMaxLinksPerSource(string[] args)
{
    var value = GetOptionValue(args, "--max-links-per-source");
    return int.TryParse(value, out var parsedValue) ? parsedValue : 20;
}

static string ResolveSearchQualityQuerySetPath(string[] args)
{
    var explicitPath = GetOptionValue(args, "--queries");
    if (!string.IsNullOrWhiteSpace(explicitPath))
    {
        return explicitPath;
    }

    return Path.Combine(ResolveProjectRoot(), "data", "search-quality", "cnv.search-quality.json");
}

static string ResolveAnalysisQualityCaseSetPath(string[] args)
{
    var explicitPath = GetOptionValue(args, "--cases");
    if (!string.IsNullOrWhiteSpace(explicitPath))
    {
        return explicitPath;
    }

    return Path.Combine(ResolveProjectRoot(), "data", "analysis-quality", "cnv.analysis-quality.json");
}

static int ResolveSearchQualityLimit(string[] args)
{
    var value = GetOptionValue(args, "--limit");
    return int.TryParse(value, out var parsedValue) ? parsedValue : 5;
}

static string ResolveSearchMode(string[] args) =>
    GetOptionValue(args, "--mode") ?? GetOptionValue(args, "--search-mode") ?? "full_text";

static IReadOnlyList<string> ResolveCompareSearchModes(string[] args)
{
    var value = GetOptionValue(args, "--compare-modes");
    if (string.IsNullOrWhiteSpace(value))
    {
        return [];
    }

    return value
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(mode => !string.IsNullOrWhiteSpace(mode))
        .Take(2)
        .ToArray();
}

static ExplainSearchQueryRequest ResolveExplainSearchQueryRequest(string[] args) =>
    new()
    {
        Query = ResolveSearchQuery(args),
        Area = GetOptionValue(args, "--area"),
        Limit = ResolveSearchQualityLimit(args),
        Source = GetOptionValue(args, "--source"),
        DocumentType = GetOptionValue(args, "--document-type"),
        ResolutionNumber = GetOptionValue(args, "--resolution-number"),
        Status = GetOptionValue(args, "--status"),
        RequiresReview = ResolveNullableBoolOption(args, "--requires-review"),
        IncludeDuplicates = HasOption(args, "--include-duplicates"),
        IncludeNonSearchable = HasOption(args, "--include-non-searchable")
    };

static string ResolveSearchQuery(string[] args)
{
    var explicitQuery = GetOptionValue(args, "--query");
    if (!string.IsNullOrWhiteSpace(explicitQuery))
    {
        return explicitQuery.Trim();
    }

    return string.Join(' ', GetPositionalArguments(args)).Trim();
}

static bool? ResolveNullableBoolOption(string[] args, string optionName)
{
    var value = GetOptionValue(args, optionName);
    return bool.TryParse(value, out var parsedValue) ? parsedValue : null;
}

static bool ResolveBoolOption(string[] args, string optionName, bool defaultValue)
{
    var value = GetOptionValue(args, optionName);
    if (bool.TryParse(value, out var parsedValue))
    {
        return parsedValue;
    }

    return HasOption(args, optionName) || defaultValue;
}

static int? ResolveNullableIntOption(string[] args, string optionName)
{
    var value = GetOptionValue(args, optionName);
    return int.TryParse(value, out var parsedValue) ? parsedValue : null;
}

static string ResolveOutputDirectory(string[] args)
{
    var explicitOutputDirectory = GetOptionValue(args, "--output-directory");
    if (!string.IsNullOrWhiteSpace(explicitOutputDirectory))
    {
        return explicitOutputDirectory;
    }

    return Path.Combine(ResolveProjectRoot(), "data", "sources");
}

static string? GetOptionValue(string[] args, string optionName)
{
    var optionIndex = Array.FindIndex(args, argument =>
        string.Equals(argument, optionName, StringComparison.OrdinalIgnoreCase));

    return optionIndex >= 0 && args.Length > optionIndex + 1 ? args[optionIndex + 1] : null;
}

static bool HasOption(string[] args, string optionName) =>
    args.Any(argument => string.Equals(argument, optionName, StringComparison.OrdinalIgnoreCase));

static IEnumerable<string> GetPositionalArguments(string[] args)
{
    for (var index = 1; index < args.Length; index++)
    {
        var argument = args[index];
        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            yield return argument;
            continue;
        }

        if (ArgumentHasValue(args, index))
        {
            index++;
        }
    }
}

static bool ArgumentHasValue(string[] args, int index) =>
    args.Length > index + 1
    && !args[index + 1].StartsWith("--", StringComparison.Ordinal);

static string ResolveProjectRoot() =>
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
