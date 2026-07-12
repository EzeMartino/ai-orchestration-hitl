using System.ComponentModel;
using System.Diagnostics;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;

internal interface ILocalPdfToolRunner
{
    Task RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        long? maximumWorkingDirectoryBytes = null,
        string? workingDirectory = null,
        long? maximumToolOutputBytes = null);
}

internal enum LocalPdfToolFailure
{
    DependencyMissing,
    ToolFailed,
    Timeout,
    ResourceLimitExceeded
}

internal sealed class LocalPdfToolException(
    LocalPdfToolFailure failure) : Exception
{
    public LocalPdfToolFailure Failure { get; } = failure;
}

internal sealed class LocalPdfToolRunner : ILocalPdfToolRunner
{
    private static readonly TimeSpan PostKillWaitTimeout =
        TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WorkingDirectoryPollInterval =
        TimeSpan.FromMilliseconds(50);

    public async Task RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        long? maximumWorkingDirectoryBytes = null,
        string? workingDirectory = null,
        long? maximumToolOutputBytes = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new LocalPdfToolException(LocalPdfToolFailure.DependencyMissing);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                throw new LocalPdfToolException(
                    LocalPdfToolFailure.DependencyMissing);
            }
        }
        catch (Win32Exception)
        {
            throw new LocalPdfToolException(LocalPdfToolFailure.DependencyMissing);
        }
        catch (FileNotFoundException)
        {
            throw new LocalPdfToolException(LocalPdfToolFailure.DependencyMissing);
        }

        using var timeoutCts = new CancellationTokenSource(
            timeout > TimeSpan.Zero
                ? timeout
                : TimeSpan.FromMilliseconds(1));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        var outputLimit = maximumToolOutputBytes is { } outputBytes
            ? new ToolOutputLimit(Math.Max(0, outputBytes))
            : null;
        var stdoutTask = DrainOutputAsync(
            process.StandardOutput.BaseStream,
            outputLimit,
            linkedCts.Token);
        var stderrTask = DrainOutputAsync(
            process.StandardError.BaseStream,
            outputLimit,
            linkedCts.Token);
        var exitTask = process.WaitForExitAsync(linkedCts.Token);

        try
        {
            await WaitForCompletionAsync(
                exitTask,
                stdoutTask,
                stderrTask,
                maximumWorkingDirectoryBytes,
                workingDirectory,
                linkedCts.Token);
        }
        catch (ResourceLimitExceededException)
        {
            await KillProcessTreeAndObserveAsync(
                process,
                stdoutTask,
                stderrTask);
            throw new LocalPdfToolException(
                LocalPdfToolFailure.ResourceLimitExceeded);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            await KillProcessTreeAndObserveAsync(
                process,
                stdoutTask,
                stderrTask);
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (Exception) when (timeoutCts.IsCancellationRequested)
        {
            await KillProcessTreeAndObserveAsync(
                process,
                stdoutTask,
                stderrTask);
            throw new LocalPdfToolException(LocalPdfToolFailure.Timeout);
        }
        catch
        {
            await KillProcessTreeAndObserveAsync(
                process,
                stdoutTask,
                stderrTask);
            throw;
        }

        if (process.ExitCode == 0)
        {
            return;
        }

        throw new LocalPdfToolException(LocalPdfToolFailure.ToolFailed);
    }

    private static async Task WaitForCompletionAsync(
        Task exitTask,
        Task stdoutTask,
        Task stderrTask,
        long? maximumWorkingDirectoryBytes,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfFaulted(stdoutTask);
            ThrowIfFaulted(stderrTask);

            if (exitTask.IsCompleted)
            {
                await Task.WhenAll(exitTask, stdoutTask, stderrTask);
                return;
            }

            var waitTasks = new List<Task> { exitTask };
            if (!stdoutTask.IsCompleted)
            {
                waitTasks.Add(stdoutTask);
            }

            if (!stderrTask.IsCompleted)
            {
                waitTasks.Add(stderrTask);
            }

            if (maximumWorkingDirectoryBytes is { } maximumBytes
                && !string.IsNullOrWhiteSpace(workingDirectory))
            {
                EnsureWorkingDirectoryWithinLimit(
                    workingDirectory,
                    Math.Max(0, maximumBytes));
                waitTasks.Add(
                    Task.Delay(WorkingDirectoryPollInterval, cancellationToken));
            }

            await Task.WhenAny(waitTasks);
        }
    }

    private static void ThrowIfFaulted(Task task)
    {
        if (task.IsFaulted)
        {
            task.GetAwaiter().GetResult();
        }
    }

    private static async Task DrainOutputAsync(
        Stream stream,
        ToolOutputLimit? outputLimit,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16_384];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return;
            }

            outputLimit?.Consume(read);
        }
    }

    private static void EnsureWorkingDirectoryWithinLimit(
        string workingDirectory,
        long maximumBytes)
    {
        long totalBytes = 0;
        foreach (var path in Directory.EnumerateFiles(
                     workingDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            var fileLength = new FileInfo(path).Length;
            if (fileLength > maximumBytes - totalBytes)
            {
                throw new ResourceLimitExceededException();
            }

            totalBytes += fileLength;
        }
    }

    private static async Task KillProcessTreeAndObserveAsync(
        Process process,
        Task stdoutTask,
        Task stderrTask)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Win32Exception)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }

        using var cleanupCts =
            new CancellationTokenSource(PostKillWaitTimeout);

        var exitTask = ObserveTaskAsync(
            WaitForExitAfterKillAsync(process, cleanupCts.Token));
        var observeReadersTask = Task.WhenAll(
            ObserveTaskAsync(stdoutTask),
            ObserveTaskAsync(stderrTask));

        try
        {
            await Task.WhenAll(exitTask, observeReadersTask)
                .WaitAsync(cleanupCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task WaitForExitAfterKillAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!process.HasExited)
            {
                await process.WaitForExitAsync(cancellationToken);
            }
        }
        catch (Win32Exception)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    private static async Task ObserveTaskAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception)
        {
            // Cleanup must observe background faults without masking the
            // caller cancellation or timeout that triggered process teardown.
        }
    }

    private sealed class ResourceLimitExceededException : Exception;

    private sealed class ToolOutputLimit(long maximumBytes)
    {
        private long _consumedBytes;

        public void Consume(int bytes)
        {
            if (Interlocked.Add(ref _consumedBytes, bytes) > maximumBytes)
            {
                throw new ResourceLimitExceededException();
            }
        }
    }
}
