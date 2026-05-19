using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Shared;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis;

public sealed class FixtureStructuredFinancialMetricsProvider : IStructuredFinancialMetricsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DataAgentOptions _options;
    private readonly ILogger<FixtureStructuredFinancialMetricsProvider> _logger;

    public FixtureStructuredFinancialMetricsProvider(
        IOptions<DataAgentOptions> options,
        ILogger<FixtureStructuredFinancialMetricsProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StructuredFinancialMetricsDocument?> GetMetricsAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken)
    {
        var fixturePath = ResolveFixturePath();

        if (fixturePath is null)
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(fixturePath);
            var fixture = await JsonSerializer.DeserializeAsync<VistaEnergyFixture>(
                stream,
                JsonOptions,
                cancellationToken
            );

            return fixture is null
                ? null
                : new StructuredFinancialMetricsDocument(
                    DocumentId: Path.GetFileNameWithoutExtension(fixturePath),
                    Company: fixture.Company,
                    Currency: fixture.Currency,
                    Unit: fixture.Unit,
                    Metrics: fixture.Metrics
                );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(
                ex,
                "Structured financial metrics fixture could not be loaded."
            );

            return null;
        }
    }

    private string? ResolveFixturePath()
    {
        if (!string.IsNullOrWhiteSpace(_options.StructuredMetricsFixturePath))
        {
            var configuredPath = Path.GetFullPath(_options.StructuredMetricsFixturePath);

            return File.Exists(configuredPath)
                ? configuredPath
                : null;
        }

        return GetSearchRoots()
            .SelectMany(GetFixtureCandidates)
            .FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> GetSearchRoots()
    {
        yield return AppContext.BaseDirectory;
        yield return Directory.GetCurrentDirectory();
    }

    private static IEnumerable<string> GetFixtureCandidates(string startPath)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startPath));

        while (directory is not null)
        {
            yield return Path.Combine(
                directory.FullName,
                "Fixtures",
                "vista_energy_sample_metrics.json"
            );
            yield return Path.Combine(
                directory.FullName,
                "backend",
                "Orchestration.Tests",
                "Fixtures",
                "vista_energy_sample_metrics.json"
            );

            directory = directory.Parent;
        }
    }

    private sealed record VistaEnergyFixture(
        string Company,
        string Source,
        string Currency,
        string Unit,
        IReadOnlyList<string> Periods,
        IReadOnlyList<FinancialMetric> Metrics
    );
}
