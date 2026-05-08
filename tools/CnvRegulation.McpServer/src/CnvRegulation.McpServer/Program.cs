using CnvRegulation.McpServer;
using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddCnvRegulationMcpServices();

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
    var explicitDirectory = args
        .Skip(1)
        .FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));

    if (!string.IsNullOrWhiteSpace(explicitDirectory))
    {
        return explicitDirectory;
    }

    var switchIndex = Array.FindIndex(args, argument =>
        string.Equals(argument, "--source-directory", StringComparison.OrdinalIgnoreCase));

    if (switchIndex >= 0 && args.Length > switchIndex + 1)
    {
        return args[switchIndex + 1];
    }

    return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "sources");
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

    var positionalManifestPath = args
        .Skip(1)
        .FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));

    if (!string.IsNullOrWhiteSpace(positionalManifestPath))
    {
        return positionalManifestPath;
    }

    return Path.Combine(ResolveProjectRoot(), "data", "source-manifest", "sources.manifest.json");
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

static string ResolveProjectRoot() =>
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
