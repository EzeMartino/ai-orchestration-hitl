using System.ComponentModel;
using System.Diagnostics;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;

internal interface ILocalPdfToolRunner
{
    Task RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal enum LocalPdfToolFailure
{
    DependencyMissing,
    ToolFailed,
    Timeout
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

    public async Task RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
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

        var stdoutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);
        var exitTask = process.WaitForExitAsync(linkedCts.Token);

        try
        {
            await Task.WhenAll(exitTask, stdoutTask, stderrTask);
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

        if (process.ExitCode == 0)
        {
            return;
        }

        throw new LocalPdfToolException(LocalPdfToolFailure.ToolFailed);
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
}
