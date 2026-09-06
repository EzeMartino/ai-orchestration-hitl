using CSnakes.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orchestration.Application.Activity;
using Orchestration.Application.Agents.Data;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.AiReview;
using Orchestration.Infrastructure.Agents.Data;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis;
using Orchestration.Tests.Agents;

namespace Orchestration.Tests.Agents.Data;

[CollectionDefinition(CollectionName)]
public sealed class PythonAgentTestCollection : ICollectionFixture<PythonAgentTestFixture>
{
    public const string CollectionName = "Python agent tests";
}

public sealed class PythonAgentTestFixture : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public PythonAgentTestFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var pythonHome = GetPythonHome();
        var pythonLockFile = Path.Combine(pythonHome, "requirements.lock");

        if (!File.Exists(pythonLockFile))
        {
            throw new FileNotFoundException(
                "Python dependency lock file was not found. "
                + "Generate requirements.lock from requirements.txt before running Python-backed tests.",
                pythonLockFile
            );
        }

        services
            .WithPython()
            .WithHome(pythonHome)
            .FromRedistributable()
            .WithVirtualEnvironment(Path.Combine(pythonHome, ".venv"))
            .WithPipInstaller(pythonLockFile);

        services.AddSingleton<Orchestration.Application.FinancialAnalysis.Thresholds.IFinancialRiskThresholdProfileProvider, Orchestration.Application.FinancialAnalysis.Thresholds.InMemoryFinancialRiskThresholdProfileProvider>();
        services.AddScoped<CSnakesDataAgent>();
        services.AddPythonFinancialAnalysis();
        services.AddSingleton(Options.Create(new DataAgentOptions()));
        services.AddScoped<ILegacyDataAgent>(provider =>
            provider.GetRequiredService<CSnakesDataAgent>());
        services.AddScoped<IStructuredFinancialMetricsProvider, FixtureStructuredFinancialMetricsProvider>();
        services.AddScoped<DeterministicDataAgentAiReviewService>();
        services.AddScoped<IDataAgentAiReviewService>(provider =>
            provider.GetRequiredService<DeterministicDataAgentAiReviewService>());
        services.AddScoped<IActivityEventPublisher, FakeActivityEventPublisher>();
        services.AddScoped<IDataAgentFinancialAnalysisWorkflow, DataAgentFinancialAnalysisWorkflow>();
        services.AddScoped<IDataAgent, ConfigurableDataAgent>();

        _serviceProvider = services.BuildServiceProvider();
    }

    public T GetRequiredService<T>()
        where T : notnull
    {
        return _serviceProvider.GetRequiredService<T>();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    private static string GetPythonHome()
    {
        var configuredPath = Environment.GetEnvironmentVariable("ORCHESTRATION_TEST_PYTHON_HOME");

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var fullConfiguredPath = ResolvePythonHome(configuredPath);

            if (!Directory.Exists(fullConfiguredPath))
            {
                throw new DirectoryNotFoundException(
                    $"ORCHESTRATION_TEST_PYTHON_HOME points to a directory that does not exist: {fullConfiguredPath}"
                );
            }

            return fullConfiguredPath;
        }

        var candidates = new[]
            {
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory()
            }
            .SelectMany(GetPythonHomeCandidates)
            .Select(ResolvePythonHome)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var pythonHome = candidates.FirstOrDefault(Directory.Exists);

        if (pythonHome is null)
        {
            throw new DirectoryNotFoundException(
                $"Python home directory was not found. Set ORCHESTRATION_TEST_PYTHON_HOME. Tried: {string.Join(", ", candidates)}"
            );
        }

        return pythonHome;
    }

    private static IEnumerable<string> GetPythonHomeCandidates(string startPath)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startPath));

        while (directory is not null)
        {
            yield return Path.Combine(directory.FullName, "python-agents");
            directory = directory.Parent;
        }
    }

    private static string ResolvePythonHome(string pythonHome)
    {
        var resolvedPath = Path.GetFullPath(pythonHome);

        if (File.Exists(Path.Combine(resolvedPath, "anomaly_detection.py")))
        {
            return resolvedPath;
        }

        var dataAgentPath = Path.Combine(resolvedPath, "data_agent");

        if (File.Exists(Path.Combine(dataAgentPath, "anomaly_detection.py")))
        {
            return dataAgentPath;
        }

        return resolvedPath;
    }
}
