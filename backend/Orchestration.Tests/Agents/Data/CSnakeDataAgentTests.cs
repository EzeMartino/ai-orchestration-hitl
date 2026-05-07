using CSnakes.Runtime;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Orchestration.Application.Agents.Shared;
using Orchestration.Infrastructure.Agents.Data;

namespace Orchestration.Tests.Agents.Data;

public class CSnakesDataAgentTests
{
    [Fact]
    public async Task AnalyzeAsync_Should_return_python_generated_anomaly_result()
    {
        var pythonHome = GetPythonHome();

        var services = new ServiceCollection();

        services
            .WithPython()
            .WithHome(pythonHome)
            .FromRedistributable();

        services.AddScoped<CSnakesDataAgent>();

        await using var serviceProvider = services.BuildServiceProvider();

        var agent = serviceProvider.GetRequiredService<CSnakesDataAgent>();

        var report = new FinancialReportContext(
            SessionId: Guid.NewGuid(),
            ReportName: "test-report",
            TotalAmount: 125000m,
            TransactionCount: 42,
            SubmittedAt: DateTimeOffset.UtcNow
        );

        var result = await agent.AnalyzeAsync(
            report,
            CancellationToken.None
        );

        result.Engine.Should().Be("Python/CSnakes");
        result.HasAnomaly.Should().BeTrue();
        result.Severity.Should().Be("High");
        result.Evidence.Should().HaveCount(2);

        result.Evidence
            .Should()
            .Contain(x =>
                x.Metric == "TransactionAmountZScore" &&
                x.Value > x.Threshold
            );

        result.Evidence
            .Should()
            .Contain(x =>
                x.Metric == "VelocityScore" &&
                x.Value > x.Threshold
            );
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
