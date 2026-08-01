using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace Orchestration.Tests.Integration;

[Collection(ProductionIntegrationCollection.Name)]
public sealed class RequiredMcpReadinessTests(ITestOutputHelper output)
{
    private static readonly TimeSpan ProcessTerminationTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProcessDrainCleanupTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ContainerStartTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ContainerStopTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ContainerDisposeTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HealthRequestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    [Trait("Category", "ProductionIntegration")]
    public async Task RequiredMcp_ProductionTracksDisposablePostgresAndRejectsInvalidCommand()
    {
        using var ambientCnvPoison = new EnvironmentVariableScope(
            ("CNV_REGULATION_DB_CONNECTION_STRING",
                "Host=127.0.0.1;Port=1;Database=task11_poison;Username=task11_poison;Timeout=1;Command Timeout=1"));

        var identity = Guid.NewGuid().ToString("N");
        var appDatabaseName = $"app_{identity[..16]}";
        var cnvDatabaseName = $"cnv_{identity[..16]}";
        var appDatabase = CreatePostgresContainer(
            $"aihitl-task11-app-mcp-{identity}",
            appDatabaseName,
            "postgres:17-alpine");
        var cnvDatabase = CreatePostgresContainer(
            $"aihitl-task11-cnv-{identity}",
            cnvDatabaseName,
            "pgvector/pgvector:pg17");
        string? publishedDirectory = null;
        ExceptionDispatchInfo? primaryFailure = null;
        var cleanupFailures = new List<Exception>();

        try
        {
            await StartContainerAsync(
                appDatabase,
                "Task 11 app container start");
            await StartContainerAsync(
                cnvDatabase,
                "Task 11 CNV container start");
            WriteDisposableIdentity("mcp-app", appDatabase, appDatabaseName);
            WriteDisposableIdentity("mcp-cnv", cnvDatabase, cnvDatabaseName);

            publishedDirectory = CreateTask11PublishDirectory();
            var publishedMcp = await PublishCurrentPlatformMcpAsync(publishedDirectory);
            var cnvConnectionString = cnvDatabase.GetConnectionString();
            await RunMcpMigrationAsync(
                publishedMcp.CommandPath,
                cnvConnectionString);

            using var cnvEnvironment = new EnvironmentVariableScope(
                ("CNV_REGULATION_DB_CONNECTION_STRING", cnvConnectionString),
                ("RegulationDb__ConnectionString", cnvConnectionString),
                ("Embeddings__Enabled", "false"),
                ("Embeddings__Provider", "Fake"),
                ("Embeddings__ApiKey", null));

            await using (var factory = new RequiredMcpWebApplicationFactory(
                appDatabase.GetConnectionString(),
                publishedMcp.CommandPath,
                ["--storage", "postgres"]))
            {
                using var client = factory.CreateClient(CreateClientOptions());

                var healthy = await GetHealthObservationAsync(
                    client,
                    HealthRequestTimeout,
                    "Task 11 initial health request");
                Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
                Assert.Equal("Healthy", healthy.Body);

                var invalidCommand = Path.Combine(
                    Path.GetTempPath(),
                    $"aihitl-task11-missing-{Guid.NewGuid():N}",
                    Path.GetFileName(publishedMcp.CommandPath));
                await using var invalidFactory = new RequiredMcpWebApplicationFactory(
                    appDatabase.GetConnectionString(),
                    invalidCommand,
                    ["--storage", "postgres"]);

                var startupFailure = await Record.ExceptionAsync(async () =>
                {
                    using var invalidClient = invalidFactory.CreateClient(CreateClientOptions());
                    await GetHealthObservationAsync(
                        invalidClient,
                        HealthRequestTimeout,
                        "Task 11 invalid-command health request");
                });

                Assert.NotNull(startupFailure);
                Assert.Contains(
                    EnumerateExceptionMessages(startupFailure),
                    message => message.Contains(
                        "CNV MCP readiness probe failed.",
                        StringComparison.Ordinal));

                await StopContainerAsync(
                    cnvDatabase,
                    "Task 11 CNV container stop");

                var unhealthy = await PollForUnhealthyAsync(client, TimeSpan.FromSeconds(20));
                Assert.Equal(HttpStatusCode.ServiceUnavailable, unhealthy.StatusCode);
                Assert.Equal("Unhealthy", unhealthy.Body);
            }
        }
        catch (Exception exception)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            await CaptureCleanupFailureAsync(
                () => DisposeContainerAsync(
                    cnvDatabase,
                    "Task 11 CNV container dispose"),
                "Task 11 CNV container dispose",
                cleanupFailures);
            await CaptureCleanupFailureAsync(
                () => DisposeContainerAsync(
                    appDatabase,
                    "Task 11 app container dispose"),
                "Task 11 app container dispose",
                cleanupFailures);
            await CaptureCleanupFailureAsync(
                () => DeletePublishedDirectoryAsync(publishedDirectory),
                "Task 11 MCP publish directory cleanup",
                cleanupFailures);
        }

        ThrowPrimaryOrCleanupFailures(primaryFailure, cleanupFailures);
    }

    [Fact]
    [Trait("Category", "ProductionIntegration")]
    public async Task RunProcessAsync_WhenTimeoutExpires_KillsTheEntireProcessTree()
    {
        var markerFileName = $"aihitl-task11-timeout-{Guid.NewGuid():N}.marker";
        var startedMarkerFileName =
            $"aihitl-task11-timeout-started-{Guid.NewGuid():N}.marker";
        var childScriptFileName =
            $"aihitl-task11-timeout-child-{Guid.NewGuid():N}"
            + (OperatingSystem.IsWindows() ? ".cmd" : ".sh");
        var temporaryDirectory = Path.GetTempPath();
        var markerPath = Path.Combine(temporaryDirectory, markerFileName);
        var startedMarkerPath = Path.Combine(temporaryDirectory, startedMarkerFileName);
        var childScriptPath = Path.Combine(temporaryDirectory, childScriptFileName);
        var startInfo = CreateProcessStartInfo(
            OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh");
        startInfo.WorkingDirectory = temporaryDirectory;
        startInfo.Environment["TASK11_TIMEOUT_MARKER"] = markerPath;
        startInfo.Environment["TASK11_TIMEOUT_STARTED_MARKER"] = startedMarkerPath;
        startInfo.Environment["TASK11_TIMEOUT_CHILD_SCRIPT"] = childScriptFileName;

        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(
                "cmd.exe /d /s /c %TASK11_TIMEOUT_CHILD_SCRIPT%");
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(
                "/bin/sh \"$TASK11_TIMEOUT_CHILD_SCRIPT\"");
        }

        try
        {
            var childScript = OperatingSystem.IsWindows()
                ? "@echo off\r\n"
                  + ">\"%TASK11_TIMEOUT_STARTED_MARKER%\" echo started\r\n"
                  + "ping -n 4 127.0.0.1 >nul\r\n"
                  + ">\"%TASK11_TIMEOUT_MARKER%\" echo completed\r\n"
                : "#!/bin/sh\n"
                  + ": > \"$TASK11_TIMEOUT_STARTED_MARKER\"\n"
                  + "sleep 3\n"
                  + ": > \"$TASK11_TIMEOUT_MARKER\"\n";
            await File.WriteAllTextAsync(childScriptPath, childScript);

            await Assert.ThrowsAsync<TimeoutException>(() =>
                RunProcessAsync(
                    startInfo,
                    "Task 11 process timeout probe",
                    TimeSpan.FromSeconds(1)));

            Assert.True(File.Exists(startedMarkerPath));
            await Task.Delay(TimeSpan.FromSeconds(4));
            Assert.False(File.Exists(markerPath));
        }
        finally
        {
            if (File.Exists(markerPath))
            {
                File.Delete(markerPath);
            }

            if (File.Exists(startedMarkerPath))
            {
                File.Delete(startedMarkerPath);
            }

            if (File.Exists(childScriptPath))
            {
                File.Delete(childScriptPath);
            }
        }
    }

    [Fact]
    [Trait("Category", "ProductionIntegration")]
    public async Task AwaitTaskWithDeadlineAsync_WhenTaskNeverCompletes_ThrowsWithinDeadline()
    {
        var neverCompletes = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stopwatch = Stopwatch.StartNew();

        var failure = await Assert.ThrowsAsync<TimeoutException>(() =>
            AwaitTaskWithDeadlineAsync(
                neverCompletes.Task,
                TimeSpan.FromMilliseconds(100),
                "Task 11 bounded task probe"));

        Assert.Equal("Task 11 bounded task probe timed out.", failure.Message);
        Assert.InRange(
            stopwatch.Elapsed,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    [Trait("Category", "ProductionIntegration")]
    public async Task DrainProcessOutputAsync_WhenReaderNeverCompletes_ThrowsWithinDeadline()
    {
        var neverCompletes = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var failure = await Assert.ThrowsAsync<TimeoutException>(() =>
            DrainProcessOutputAsync(
                neverCompletes.Task,
                Task.CompletedTask,
                deadline.Token,
                "Task 11 normal drain probe"));

        Assert.Equal(
            "Task 11 normal drain probe output drain timed out.",
            failure.Message);
    }

    [Fact]
    [Trait("Category", "ProductionIntegration")]
    public void ThrowPrimaryOrCleanupFailures_WhenBothExist_PreservesBoth()
    {
        var primary = new InvalidOperationException("Task 11 primary probe.");
        var cleanup = new TimeoutException("Task 11 cleanup probe.");

        var failure = Assert.Throws<AggregateException>(() =>
            ThrowPrimaryOrCleanupFailures(
                ExceptionDispatchInfo.Capture(primary),
                [cleanup]));

        Assert.Collection(
            failure.InnerExceptions,
            exception => Assert.Same(primary, exception),
            exception => Assert.Same(cleanup, exception));
    }

    private static async Task AwaitTaskWithDeadlineAsync(
        Task task,
        TimeSpan timeout,
        string operation)
    {
        using var deadline = new CancellationTokenSource(timeout);
        await AwaitTaskWithDeadlineAsync(task, deadline.Token, operation);
    }

    private static async Task AwaitTaskWithDeadlineAsync(
        Task task,
        CancellationToken deadline,
        string operation)
    {
        ObserveTask(task);

        try
        {
            await task.WaitAsync(deadline);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException($"{operation} timed out.");
        }
    }

    private static async Task<TResult> AwaitTaskWithDeadlineAsync<TResult>(
        Task<TResult> task,
        CancellationToken deadline,
        string operation)
    {
        ObserveTask(task);

        try
        {
            return await task.WaitAsync(deadline);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException($"{operation} timed out.");
        }
    }

    private static void ObserveTask(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously
            | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private static void ThrowPrimaryOrCleanupFailures(
        ExceptionDispatchInfo? primaryFailure,
        IReadOnlyCollection<Exception> cleanupFailures)
    {
        if (primaryFailure is null)
        {
            if (cleanupFailures.Count > 0)
            {
                throw new AggregateException(
                    "Task 11 cleanup failed.",
                    cleanupFailures);
            }

            return;
        }

        if (cleanupFailures.Count == 0)
        {
            primaryFailure.Throw();
            return;
        }

        throw new AggregateException(
            "Task 11 primary operation and cleanup failed.",
            new[] { primaryFailure.SourceException }.Concat(cleanupFailures));
    }

    private static WebApplicationFactoryClientOptions CreateClientOptions() => new()
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("http://localhost")
    };

    private static PostgreSqlContainer CreatePostgresContainer(
        string containerName,
        string databaseName,
        string image) =>
        new PostgreSqlBuilder(image)
            .WithName(containerName)
            .WithDatabase(databaseName)
            .WithUsername("postgres")
            .WithPassword(CreateContainerPassword())
            .WithCleanUp(true)
            .Build();

    private static async Task StartContainerAsync(
        PostgreSqlContainer container,
        string operation)
    {
        using var deadline = new CancellationTokenSource(ContainerStartTimeout);
        var start = container.StartAsync(deadline.Token);

        try
        {
            await AwaitTaskWithDeadlineAsync(start, deadline.Token, operation);
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw CreateSafeLifecycleFailure(operation, exception);
        }
    }

    private static async Task StopContainerAsync(
        PostgreSqlContainer container,
        string operation)
    {
        using var deadline = new CancellationTokenSource(ContainerStopTimeout);
        var stop = container.StopAsync(deadline.Token);

        try
        {
            await AwaitTaskWithDeadlineAsync(stop, deadline.Token, operation);
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw CreateSafeLifecycleFailure(operation, exception);
        }
    }

    private static async Task DisposeContainerAsync(
        PostgreSqlContainer container,
        string operation)
    {
        var dispose = container.DisposeAsync().AsTask();

        try
        {
            await AwaitTaskWithDeadlineAsync(
                dispose,
                ContainerDisposeTimeout,
                operation);
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw CreateSafeLifecycleFailure(operation, exception);
        }
    }

    private static async Task CaptureCleanupFailureAsync(
        Func<Task> cleanup,
        string operation,
        ICollection<Exception> cleanupFailures)
    {
        try
        {
            await cleanup();
        }
        catch (Exception exception)
        {
            cleanupFailures.Add(CreateSafeLifecycleFailure(operation, exception));
        }
    }

    private static string CreateContainerPassword() =>
        $"T11-{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}-aA1!";

    private static string CreateTask11PublishDirectory()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"aihitl-task11-mcp-{Guid.NewGuid():N}");
        var normalizedOutput = ValidateTask11PublishDirectory(outputDirectory);
        Directory.CreateDirectory(normalizedOutput);
        return normalizedOutput;
    }

    private static async Task<PublishedMcp> PublishCurrentPlatformMcpAsync(
        string outputDirectory)
    {
        outputDirectory = ValidateTask11PublishDirectory(outputDirectory);
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "tools",
            "CnvRegulation.McpServer",
            "src",
            "CnvRegulation.McpServer",
            "CnvRegulation.McpServer.csproj");
        var startInfo = CreateProcessStartInfo("dotnet");
        startInfo.WorkingDirectory = repositoryRoot;
        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--runtime");
        startInfo.ArgumentList.Add(ResolveCurrentRuntimeIdentifier());
        startInfo.ArgumentList.Add("--self-contained");
        startInfo.ArgumentList.Add("true");
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputDirectory);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("--disable-build-servers");

        await RunProcessAsync(
            startInfo,
            "MCP publish",
            TimeSpan.FromMinutes(2));

        var commandPath = Path.Combine(
            outputDirectory,
            OperatingSystem.IsWindows()
                ? "CnvRegulation.McpServer.exe"
                : "CnvRegulation.McpServer");
        if (!File.Exists(commandPath))
        {
            throw new InvalidOperationException(
                "MCP publish did not produce the expected current-platform executable.");
        }

        return new PublishedMcp(commandPath, outputDirectory);
    }

    private static async Task RunMcpMigrationAsync(
        string commandPath,
        string connectionString)
    {
        var startInfo = CreateProcessStartInfo(commandPath);
        startInfo.ArgumentList.Add("migrate-db");
        startInfo.ArgumentList.Add("--storage");
        startInfo.ArgumentList.Add("postgres");
        startInfo.Environment["CNV_REGULATION_DB_CONNECTION_STRING"] = connectionString;
        startInfo.Environment["RegulationDb__ConnectionString"] = connectionString;
        startInfo.Environment["Embeddings__Enabled"] = "false";
        startInfo.Environment["Embeddings__Provider"] = "Fake";
        startInfo.Environment.Remove("Embeddings__ApiKey");

        await RunProcessAsync(
            startInfo,
            "CNV disposable database migration",
            TimeSpan.FromSeconds(30));
    }

    private static ProcessStartInfo CreateProcessStartInfo(string command) => new(command)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    private static async Task RunProcessAsync(
        ProcessStartInfo startInfo,
        string operation,
        TimeSpan timeout)
    {
        using var executionDeadline = new CancellationTokenSource(timeout);
        using var outputCancellation = new CancellationTokenSource();
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"{operation} did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(outputCancellation.Token);
        var standardError = process.StandardError.ReadToEndAsync(outputCancellation.Token);
        ObserveTask(standardOutput);
        ObserveTask(standardError);

        var executionTimedOut = false;

        try
        {
            await process.WaitForExitAsync(executionDeadline.Token);
        }
        catch (OperationCanceledException) when (executionDeadline.IsCancellationRequested)
        {
            executionTimedOut = true;
        }

        if (executionTimedOut)
        {
            var cleanupFailures = await TerminateAndDrainProcessAsync(
                process,
                outputCancellation,
                standardOutput,
                standardError);
            throw CreateProcessTimeoutException(operation, cleanupFailures);
        }

        try
        {
            await DrainProcessOutputAsync(
                standardOutput,
                standardError,
                executionDeadline.Token,
                operation);
        }
        catch (TimeoutException)
        {
            var cleanupFailures = await TerminateAndDrainProcessAsync(
                process,
                outputCancellation,
                standardOutput,
                standardError);
            throw CreateProcessTimeoutException(operation, cleanupFailures);
        }
        catch (Exception exception)
            when (exception is IOException
                  or ObjectDisposedException
                  or OperationCanceledException)
        {
            var cleanupFailures = await TerminateAndDrainProcessAsync(
                process,
                outputCancellation,
                standardOutput,
                standardError);
            cleanupFailures.Insert(
                0,
                CreateSafeLifecycleFailure($"{operation} output drain", exception));
            throw new InvalidOperationException(
                $"{operation} output drain failed.",
                new AggregateException(
                    "Process output cleanup failed.",
                    cleanupFailures));
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{operation} failed with exit code {process.ExitCode}.");
        }
    }

    private static Task DrainProcessOutputAsync(
        Task standardOutput,
        Task standardError,
        CancellationToken deadline,
        string operation) =>
        AwaitTaskWithDeadlineAsync(
            Task.WhenAll(standardOutput, standardError),
            deadline,
            $"{operation} output drain");

    private static async Task<List<Exception>> TerminateAndDrainProcessAsync(
        Process process,
        CancellationTokenSource outputCancellation,
        Task standardOutput,
        Task standardError)
    {
        var cleanupFailures = new List<Exception>();

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                  or NotSupportedException
                  or Win32Exception)
        {
            cleanupFailures.Add(
                CreateSafeLifecycleFailure("Process tree termination", exception));
        }

        await WaitForProcessExitAfterKillAsync(process, cleanupFailures);
        CancelAndCloseProcessOutput(process, outputCancellation, cleanupFailures);
        await DrainProcessOutputAfterCancellationAsync(
            standardOutput,
            standardError,
            cleanupFailures);

        try
        {
            if (!process.HasExited)
            {
                cleanupFailures.Add(
                    new TimeoutException("Process termination could not be verified."));
            }
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or Win32Exception)
        {
            cleanupFailures.Add(
                CreateSafeLifecycleFailure("Process termination verification", exception));
        }

        return cleanupFailures;
    }

    private static async Task WaitForProcessExitAfterKillAsync(
        Process process,
        ICollection<Exception> cleanupFailures)
    {
        using var terminationDeadline =
            new CancellationTokenSource(ProcessTerminationTimeout);
        var waitForExit = process.WaitForExitAsync(terminationDeadline.Token);
        ObserveTask(waitForExit);

        try
        {
            await waitForExit;
        }
        catch (OperationCanceledException) when (terminationDeadline.IsCancellationRequested)
        {
            cleanupFailures.Add(
                new TimeoutException("Process termination wait timed out."));
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or Win32Exception)
        {
            cleanupFailures.Add(
                CreateSafeLifecycleFailure("Process termination wait", exception));
        }
    }

    private static void CancelAndCloseProcessOutput(
        Process process,
        CancellationTokenSource outputCancellation,
        ICollection<Exception> cleanupFailures)
    {
        try
        {
            outputCancellation.Cancel();
        }
        catch (Exception exception)
            when (exception is AggregateException or ObjectDisposedException)
        {
            cleanupFailures.Add(
                CreateSafeLifecycleFailure("Process output cancellation", exception));
        }

        TryCloseProcessReader(
            process.StandardOutput,
            "Process standard output close",
            cleanupFailures);
        TryCloseProcessReader(
            process.StandardError,
            "Process standard error close",
            cleanupFailures);
    }

    private static void TryCloseProcessReader(
        StreamReader reader,
        string operation,
        ICollection<Exception> cleanupFailures)
    {
        try
        {
            reader.Close();
        }
        catch (Exception exception)
            when (exception is IOException or ObjectDisposedException)
        {
            cleanupFailures.Add(CreateSafeLifecycleFailure(operation, exception));
        }
    }

    private static async Task DrainProcessOutputAfterCancellationAsync(
        Task standardOutput,
        Task standardError,
        ICollection<Exception> cleanupFailures)
    {
        try
        {
            await AwaitTaskWithDeadlineAsync(
                Task.WhenAll(standardOutput, standardError),
                ProcessDrainCleanupTimeout,
                "Process output cleanup drain");
        }
        catch (OperationCanceledException)
        {
            // Expected after cancelling the StreamReader operations.
        }
        catch (Exception exception)
            when (exception is TimeoutException
                  or IOException
                  or ObjectDisposedException)
        {
            cleanupFailures.Add(
                CreateSafeLifecycleFailure("Process output cleanup drain", exception));
        }
    }

    private static TimeoutException CreateProcessTimeoutException(
        string operation,
        IReadOnlyCollection<Exception> cleanupFailures) =>
        cleanupFailures.Count == 0
            ? new TimeoutException($"{operation} timed out.")
            : new TimeoutException(
                $"{operation} timed out.",
                new AggregateException(
                    "Process timeout cleanup failed.",
                    cleanupFailures));

    private static Exception CreateSafeLifecycleFailure(
        string operation,
        Exception failure) =>
        failure is TimeoutException
            ? new TimeoutException($"{operation} timed out.")
            : new InvalidOperationException(
                $"{operation} failed with {failure.GetType().Name}.");

    private static string FindRepositoryRoot()
    {
        for (var candidate = new DirectoryInfo(AppContext.BaseDirectory);
             candidate is not null;
             candidate = candidate.Parent)
        {
            if (File.Exists(Path.Combine(candidate.FullName, "backend", "Orchestration.slnx"))
                && File.Exists(Path.Combine(
                    candidate.FullName,
                    "tools",
                    "CnvRegulation.McpServer",
                    "src",
                    "CnvRegulation.McpServer",
                    "CnvRegulation.McpServer.csproj")))
            {
                return candidate.FullName;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the repository root from the test assembly path.");
    }

    private static string ResolveCurrentRuntimeIdentifier()
    {
        var operatingSystem = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsLinux()
                ? "linux"
                : OperatingSystem.IsMacOS()
                    ? "osx"
                    : throw new PlatformNotSupportedException(
                        "Task 11 MCP readiness supports Windows, Linux, and macOS.");
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException(
                "Task 11 MCP readiness supports x64 and arm64 processes.")
        };

        return $"{operatingSystem}-{architecture}";
    }

    private static async Task<HealthObservation> PollForUnhealthyAsync(
        HttpClient client,
        TimeSpan timeout)
    {
        using var overallDeadline = new CancellationTokenSource(timeout);
        HealthObservation latest = default;

        while (!overallDeadline.IsCancellationRequested)
        {
            using var requestDeadline = CancellationTokenSource.CreateLinkedTokenSource(
                overallDeadline.Token);
            requestDeadline.CancelAfter(HealthRequestTimeout);

            try
            {
                latest = await GetHealthObservationAsync(
                    client,
                    requestDeadline.Token,
                    "Task 11 unhealthy health request");
            }
            catch (TimeoutException) when (overallDeadline.IsCancellationRequested)
            {
                return latest;
            }

            if (latest.StatusCode == HttpStatusCode.ServiceUnavailable
                && latest.Body == "Unhealthy")
            {
                return latest;
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(250),
                    overallDeadline.Token);
            }
            catch (OperationCanceledException)
                when (overallDeadline.IsCancellationRequested)
            {
                return latest;
            }
        }

        return latest;
    }

    private static async Task<HealthObservation> GetHealthObservationAsync(
        HttpClient client,
        TimeSpan timeout,
        string operation)
    {
        using var deadline = new CancellationTokenSource(timeout);
        return await GetHealthObservationAsync(client, deadline.Token, operation);
    }

    private static async Task<HealthObservation> GetHealthObservationAsync(
        HttpClient client,
        CancellationToken deadline,
        string operation)
    {
        var responseTask = client.GetAsync("/health", deadline);
        using var response = await AwaitTaskWithDeadlineAsync(
            responseTask,
            deadline,
            operation);
        var bodyTask = response.Content.ReadAsStringAsync(deadline);
        var body = await AwaitTaskWithDeadlineAsync(
            bodyTask,
            deadline,
            operation);
        return new HealthObservation(response.StatusCode, body);
    }

    private static IEnumerable<string> EnumerateExceptionMessages(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current.Message;
        }
    }

    private void WriteDisposableIdentity(
        string role,
        PostgreSqlContainer container,
        string databaseName)
    {
        var fingerprintMaterial = $"{container.Id}|{container.Name}|{databaseName}";
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintMaterial)))
            .ToLowerInvariant();
        output.WriteLine(
            "Disposable {0} container: id={1}; name={2}; run identity fingerprint={3}",
            role,
            container.Id,
            container.Name,
            fingerprint);
    }

    private static async Task DeletePublishedDirectoryAsync(string? outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory)
            || !Directory.Exists(outputDirectory))
        {
            return;
        }

        var normalizedOutput = ValidateTask11PublishDirectory(outputDirectory);

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            try
            {
                Directory.Delete(normalizedOutput, recursive: true);
                return;
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException
                      && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
        }
    }

    private static string ValidateTask11PublishDirectory(string outputDirectory)
    {
        var normalizedOutput = Path.GetFullPath(outputDirectory);
        var normalizedTemporaryRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.GetTempPath()));
        if (!string.Equals(
                Path.GetDirectoryName(normalizedOutput),
                normalizedTemporaryRoot,
                StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(normalizedOutput).StartsWith(
                "aihitl-task11-mcp-",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refusing to use an MCP publish directory outside the Task 11 temporary scope.");
        }

        return normalizedOutput;
    }

    private sealed class RequiredMcpWebApplicationFactory(
        string appConnectionString,
        string command,
        IReadOnlyList<string> arguments) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("ConnectionStrings:orchestrationdb", appConnectionString);
            builder.UseSetting("Cors:AllowedOrigins:0", "https://frontend.example.test");
            builder.UseSetting("Mcp:CnvRegulation:Enabled", "true");
            builder.UseSetting("Mcp:CnvRegulation:Required", "true");
            builder.UseSetting("Mcp:CnvRegulation:Command", command);
            builder.UseSetting("Mcp:CnvRegulation:ConnectionTimeoutSeconds", "10");
            builder.UseSetting("Mcp:CnvRegulation:ToolCallTimeoutSeconds", "10");
            for (var index = 0; index < arguments.Count; index++)
            {
                builder.UseSetting($"Mcp:CnvRegulation:Args:{index}", arguments[index]);
            }

            builder.UseSetting("Llm:Enabled", "false");
            builder.UseSetting("DataAgent:AiReviewEnabled", "false");
            builder.UseSetting("DataAgent:UseFixtureMetricsFallback", "false");
            builder.UseSetting("FinancialMetricsExtraction:SemanticEnrichmentEnabled", "false");
            builder.UseSetting("LegalAgent:AiReviewEnabled", "false");
            builder.UseSetting("ToolCalling:Enabled", "false");
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly IReadOnlyList<(string Name, string? Value)> _previous;

        public EnvironmentVariableScope(
            params (string Name, string? Value)[] variables)
        {
            _previous = variables
                .Select(variable => (
                    variable.Name,
                    Environment.GetEnvironmentVariable(variable.Name)))
                .ToArray();

            foreach (var variable in variables)
            {
                Environment.SetEnvironmentVariable(variable.Name, variable.Value);
            }
        }

        public void Dispose()
        {
            foreach (var variable in _previous)
            {
                Environment.SetEnvironmentVariable(variable.Name, variable.Value);
            }
        }
    }

    private sealed record PublishedMcp(
        string CommandPath,
        string OutputDirectory);

    private readonly record struct HealthObservation(
        HttpStatusCode StatusCode,
        string Body);
}
