using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Extraction;

public sealed class LocalPdfTemporaryDirectorySweeper(
    ILogger<LocalPdfTemporaryDirectorySweeper> logger) : BackgroundService
{
    internal const string DirectoryPrefix = "ai-orchestration-hitl-searchable-pdf-ocr-";
    private static readonly TimeSpan MinimumAge = TimeSpan.FromHours(1);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            SweepOnce(Path.GetTempPath(), DateTimeOffset.UtcNow, MinimumAge, logger);

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    internal static void SweepOnce(
        string temporaryRoot,
        DateTimeOffset now,
        TimeSpan minimumAge,
        ILogger logger)
    {
        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateDirectories(
                temporaryRoot,
                DirectoryPrefix + "*",
                SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not enumerate stale financial PDF directories.");
            return;
        }

        foreach (var directory in candidates)
        {
            try
            {
                var info = new DirectoryInfo(directory);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0
                    || now - info.LastWriteTimeUtc < minimumAge)
                {
                    continue;
                }

                info.Delete(recursive: true);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Could not remove stale financial PDF directory {Directory}.",
                    directory);
            }
        }
    }
}
