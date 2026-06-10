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
    public async Task RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
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

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await KillProcessTreeAndWaitForExitAsync(process);
            throw;
        }
        catch (OperationCanceledException)
        {
            await KillProcessTreeAndWaitForExitAsync(process);
            throw new LocalPdfToolException(LocalPdfToolFailure.Timeout);
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode == 0)
        {
            return;
        }

        _ = stdout;

        throw new LocalPdfToolException(
            string.IsNullOrWhiteSpace(stderr)
                ? LocalPdfToolFailure.DependencyMissing
                : LocalPdfToolFailure.ToolFailed);
    }

    private static async Task KillProcessTreeAndWaitForExitAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
