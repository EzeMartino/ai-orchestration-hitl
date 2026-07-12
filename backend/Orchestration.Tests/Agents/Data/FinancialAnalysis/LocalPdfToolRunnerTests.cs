using System.Diagnostics;
using FluentAssertions;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class LocalPdfToolRunnerTests
{
    [Fact]
    public async Task RunAsync_Should_map_started_nonzero_process_to_tool_failed()
    {
        var runner = new LocalPdfToolRunner();
        var command = CreateShellCommand(
            windowsCommand: "exit 7",
            unixCommand: "exit 7");

        var action = () => runner.RunAsync(
            command.FileName,
            command.Arguments,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<LocalPdfToolException>();
        exception.Which.Failure.Should().Be(LocalPdfToolFailure.ToolFailed);
    }

    [Fact]
    public async Task RunAsync_Should_apply_timeout_to_exit_and_stream_drains()
    {
        var runner = new LocalPdfToolRunner();
        var stopwatch = Stopwatch.StartNew();
        var command = CreateShellCommand(
            windowsCommand: """
                start "" /b powershell.exe -NoProfile -NonInteractive -Command "Start-Sleep -Seconds 5"
                exit 0
                """,
            unixCommand: "sleep 5 & exit 0");

        var action = () => runner.RunAsync(
            command.FileName,
            command.Arguments,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<LocalPdfToolException>();

        stopwatch.Stop();
        exception.Which.Failure.Should().Be(LocalPdfToolFailure.Timeout);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task RunAsync_Should_preserve_caller_cancellation()
    {
        var runner = new LocalPdfToolRunner();
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(150));
        var stopwatch = Stopwatch.StartNew();
        var command = CreateShellCommand(
            windowsCommand: "powershell.exe -NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 5\"",
            unixCommand: "sleep 5");

        var action = () => runner.RunAsync(
            command.FileName,
            command.Arguments,
            TimeSpan.FromSeconds(10),
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();

        stopwatch.Stop();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task RunAsync_Should_stop_process_when_working_directory_exceeds_budget()
    {
        var runner = new LocalPdfToolRunner();
        var workingDirectory = Directory.CreateTempSubdirectory(
            "ai-orchestration-hitl-pdf-runner-tests-").FullName;
        var scriptDirectory = Directory.CreateTempSubdirectory(
            "ai-orchestration-hitl-pdf-runner-script-tests-").FullName;
        var outputPath = Path.Combine(workingDirectory, "too-large.tmp");
        var scriptPath = Path.Combine(
            scriptDirectory,
            OperatingSystem.IsWindows() ? "write-and-sleep.ps1" : "write-and-sleep.sh");

        try
        {
            await File.WriteAllTextAsync(
                scriptPath,
                OperatingSystem.IsWindows()
                    ? "param([string]$OutputPath)\n[IO.File]::WriteAllBytes($OutputPath, (New-Object byte[] 11))\nStart-Sleep -Seconds 10"
                    : "head -c 11 /dev/zero > \"$1\"\nsleep 10");
            var command = CreateWriteAndSleepCommand(scriptPath, outputPath);
            var stopwatch = Stopwatch.StartNew();

            var action = () => runner.RunAsync(
                command.FileName,
                command.Arguments,
                TimeSpan.FromSeconds(10),
                CancellationToken.None,
                maximumWorkingDirectoryBytes: 10,
                workingDirectory: workingDirectory);

            var exception = await action.Should().ThrowAsync<LocalPdfToolException>();

            stopwatch.Stop();
            exception.Which.Failure.Should().Be(
                LocalPdfToolFailure.ResourceLimitExceeded);
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(6));
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
            Directory.Delete(scriptDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_Should_stop_process_when_output_exceeds_budget(
        bool writeToStandardError)
    {
        var runner = new LocalPdfToolRunner();
        var stopwatch = Stopwatch.StartNew();
        var command = CreateShellCommand(
            windowsCommand: writeToStandardError
                ? "(<nul set /p =xxxxxxxxxxx) 1>&2 & ping -n 11 127.0.0.1 >nul"
                : "<nul set /p =xxxxxxxxxxx & ping -n 11 127.0.0.1 >nul",
            unixCommand: writeToStandardError
                ? "head -c 11 /dev/zero >&2; sleep 10"
                : "head -c 11 /dev/zero; sleep 10");

        var action = () => runner.RunAsync(
            command.FileName,
            command.Arguments,
            TimeSpan.FromSeconds(20),
            CancellationToken.None,
            maximumToolOutputBytes: 10);

        var exception = await action.Should().ThrowAsync<LocalPdfToolException>();

        stopwatch.Stop();
        exception.Which.Failure.Should().Be(
            LocalPdfToolFailure.ResourceLimitExceeded);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(6));
    }

    [Fact]
    public async Task RunAsync_Should_kill_process_after_unexpected_monitor_failure()
    {
        var runner = new LocalPdfToolRunner();
        var workingDirectory = Directory.CreateTempSubdirectory(
            "ai-orchestration-hitl-pdf-monitor-tests-").FullName;
        var pidPath = Path.Combine(
            Path.GetTempPath(),
            $"ai-orchestration-hitl-pdf-tool-{Guid.NewGuid():N}.pid");
        var escapedPidPath = pidPath.Replace("'", "''", StringComparison.Ordinal);
        var command = CreatePidWriterCommand(pidPath, escapedPidPath);

        try
        {
            var runTask = runner.RunAsync(
                command.FileName,
                command.Arguments,
                TimeSpan.FromSeconds(65),
                CancellationToken.None,
                maximumWorkingDirectoryBytes: 1_024,
                workingDirectory: workingDirectory);
            await WaitUntilAsync(() => File.Exists(pidPath), TimeSpan.FromSeconds(5));
            Directory.Delete(workingDirectory);

            var action = async () => await runTask;
            await action.Should().ThrowAsync<DirectoryNotFoundException>();

            var processId = int.Parse((await File.ReadAllTextAsync(pidPath)).Trim());
            await WaitUntilAsync(
                () => !IsProcessAlive(processId),
                TimeSpan.FromSeconds(2));
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
            {
                Directory.Delete(workingDirectory, recursive: true);
            }

            File.Delete(pidPath);
        }
    }

    private static ProcessCommand CreateShellCommand(
        string windowsCommand,
        string unixCommand)
    {
        return OperatingSystem.IsWindows()
            ? new ProcessCommand("cmd.exe", ["/d", "/c", windowsCommand])
            : new ProcessCommand("/bin/sh", ["-c", unixCommand]);
    }

    private static ProcessCommand CreateWriteAndSleepCommand(
        string scriptPath,
        string outputPath)
    {
        return OperatingSystem.IsWindows()
            ? new ProcessCommand(
                "powershell.exe",
                [
                    "-NoProfile",
                    "-NonInteractive",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    scriptPath,
                    outputPath
                ])
            : new ProcessCommand("/bin/sh", [scriptPath, outputPath]);
    }

    private static ProcessCommand CreatePidWriterCommand(
        string pidPath,
        string escapedPidPath)
    {
        return OperatingSystem.IsWindows()
            ? new ProcessCommand(
                "powershell.exe",
                [
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    $"[IO.File]::WriteAllText('{escapedPidPath}', $PID); Start-Sleep -Seconds 60"
                ])
            : new ProcessCommand(
                "/bin/sh",
                ["-c", $"echo $$ > '{pidPath}'; sleep 60"]);
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            return !Process.GetProcessById(processId).HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Expected condition was not reached.");
            }

            await Task.Delay(25);
        }
    }

    private sealed record ProcessCommand(
        string FileName,
        IReadOnlyList<string> Arguments);
}
