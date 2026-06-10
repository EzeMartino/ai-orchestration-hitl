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

    private static ProcessCommand CreateShellCommand(
        string windowsCommand,
        string unixCommand)
    {
        return OperatingSystem.IsWindows()
            ? new ProcessCommand("cmd.exe", ["/d", "/c", windowsCommand])
            : new ProcessCommand("/bin/sh", ["-c", unixCommand]);
    }

    private sealed record ProcessCommand(
        string FileName,
        IReadOnlyList<string> Arguments);
}
