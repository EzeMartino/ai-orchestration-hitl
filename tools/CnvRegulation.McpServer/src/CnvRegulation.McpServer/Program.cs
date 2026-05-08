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
