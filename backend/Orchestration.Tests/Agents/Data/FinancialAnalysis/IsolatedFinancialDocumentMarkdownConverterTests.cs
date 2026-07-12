using System.Diagnostics;
using System.Text;
using FluentAssertions;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Extraction;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class IsolatedFinancialDocumentMarkdownConverterTests
{
    private const string SuccessResponse = """
        {
          "succeeded": true,
          "markdown": "# Income Statement",
          "truncated": false,
          "failureReason": null
        }
        """;

    [Fact]
    public async Task ConvertPdfAsync_Should_send_pdf_on_stdin_and_numeric_limits_as_arguments()
    {
        var runner = new FakeProcessRunner(new FinancialDocumentProcessResult(0, SuccessResponse, "", false));
        var converter = CreateConverter(runner);
        var pdfBytes = "%PDF-test"u8.ToArray();

        await using var pdf = new MemoryStream(pdfBytes);
        var result = await converter.ConvertPdfAsync(pdf, 4_321, 7, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        runner.Request.Should().NotBeNull();
        runner.StandardInput.Should().Equal(pdfBytes);
        runner.Request!.ArgumentList.Should().Equal(
            "-I",
            "document_markdown.py",
            "--max-characters",
            "4321",
            "--max-pages",
            "7",
            "--max-input-bytes",
            "20971520",
            "--max-memory-bytes",
            "1073741824");
        runner.Request.RedirectStandardInput.Should().BeTrue();
        runner.Request.RedirectStandardOutput.Should().BeTrue();
        runner.Request.RedirectStandardError.Should().BeTrue();
        runner.Request.UseShellExecute.Should().BeFalse();
    }

    [Fact]
    public async Task ConvertPdfAsync_Should_run_isolated_worker_with_real_markitdown()
    {
        var converter = new IsolatedFinancialDocumentMarkdownConverter(
            FindPythonHome(),
            20_971_520,
            1_073_741_824);
        await using var pdf = new MemoryStream(
            CreateMinimalPdf("Revenue 100"));

        var result = await converter.ConvertPdfAsync(
            pdf,
            10_000,
            1,
            CancellationToken.None);

        result.Succeeded.Should().BeTrue(result.FailureReason);
        result.Markdown.Should().Contain("Revenue 100");
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task ConvertPdfAsync_Should_preserve_caller_cancellation_during_process_execution()
    {
        var runner = new FakeProcessRunner(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new FinancialDocumentProcessResult(0, SuccessResponse, "", false);
        });
        var converter = CreateConverter(runner);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await using var pdf = new MemoryStream("%PDF-test"u8.ToArray());
        var action = () => converter.ConvertPdfAsync(pdf, 10_000, 2, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ConversionGate_Should_limit_concurrent_workers()
    {
        var gate = new FinancialDocumentConversionGate(1);
        using var firstLease = await gate.EnterAsync(CancellationToken.None);
        var secondEntered = false;
        var secondTask = Task.Run(async () =>
        {
            using var secondLease = await gate.EnterAsync(CancellationToken.None);
            secondEntered = true;
        });

        await Task.Delay(50);
        secondEntered.Should().BeFalse();
        firstLease.Dispose();
        await secondTask;
        secondEntered.Should().BeTrue();
    }

    [Fact]
    public async Task ConvertPdfAsync_Should_map_output_limit_to_safe_failure()
    {
        var runner = new FakeProcessRunner(
            new FinancialDocumentProcessResult(1, "partial", "diagnostic", true));
        var converter = CreateConverter(runner);

        await using var pdf = new MemoryStream("%PDF-test"u8.ToArray());
        var result = await converter.ConvertPdfAsync(pdf, 10_000, 2, CancellationToken.None);

        result.Should().BeEquivalentTo(new
        {
            Succeeded = false,
            Markdown = "",
            Truncated = false,
            FailureReason = "conversion_failed"
        });
    }

    [Fact]
    public async Task ConvertPdfAsync_Should_map_invalid_json_and_runner_errors_to_safe_failure()
    {
        var invalidConverter = CreateConverter(new FakeProcessRunner(
            new FinancialDocumentProcessResult(0, "invalid", "", false)));
        var failingConverter = CreateConverter(new FakeProcessRunner(
            (_, _) => throw new InvalidOperationException("process failed")));

        await using var invalidPdf = new MemoryStream("%PDF-test"u8.ToArray());
        await using var failingPdf = new MemoryStream("%PDF-test"u8.ToArray());
        var invalid = await invalidConverter.ConvertPdfAsync(invalidPdf, 10_000, 2, CancellationToken.None);
        var failed = await failingConverter.ConvertPdfAsync(failingPdf, 10_000, 2, CancellationToken.None);

        invalid.FailureReason.Should().Be("invalid_response");
        failed.FailureReason.Should().Be("conversion_failed");
    }

    [Fact]
    public async Task ProcessRunner_Should_kill_python_when_cancelled()
    {
        var pythonExecutable = FindPythonExecutable();
        var pidPath = Path.Combine(
            Path.GetTempPath(),
            $"ai-orchestration-hitl-markitdown-pids-{Guid.NewGuid():N}.txt");
        var startInfo = new ProcessStartInfo(pythonExecutable);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(
            "import os,pathlib,subprocess,sys,time; "
            + "child=subprocess.Popen([sys.executable,'-c','import time; time.sleep(60)']); "
            + "pathlib.Path(sys.argv[1]).write_text(f'{os.getpid()},{child.pid}'); "
            + "time.sleep(60)");
        startInfo.ArgumentList.Add(pidPath);
        ConfigureRedirects(startInfo);
        var runner = new SystemFinancialDocumentProcessRunner();
        using var cancellation = new CancellationTokenSource();
        var stopwatch = Stopwatch.StartNew();

        var runTask = runner.RunAsync(
            new FinancialDocumentProcessRequest(
                startInfo,
                new MemoryStream(),
                1_024,
                1_073_741_824,
                1_024,
                1_024),
            cancellation.Token);
        try
        {
            await WaitUntilAsync(() => File.Exists(pidPath), TimeSpan.FromSeconds(5));
            cancellation.Cancel();

            var action = async () => await runTask;
            await action.Should().ThrowAsync<OperationCanceledException>();
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));

            var processIds = (await File.ReadAllTextAsync(pidPath))
                .Split(',')
                .Select(int.Parse)
                .ToArray();
            await WaitUntilAsync(
                () => processIds.All(processId => !IsProcessAlive(processId)),
                TimeSpan.FromSeconds(2));
        }
        finally
        {
            File.Delete(pidPath);
        }
    }

    [Fact]
    public async Task ProcessRunner_Should_bound_stdout_and_stderr()
    {
        var pythonExecutable = FindPythonExecutable();
        var startInfo = new ProcessStartInfo(pythonExecutable);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("import sys; print('o' * 4096); print('e' * 4096, file=sys.stderr)");
        ConfigureRedirects(startInfo);
        var runner = new SystemFinancialDocumentProcessRunner();

        var result = await runner.RunAsync(
            new FinancialDocumentProcessRequest(
                startInfo,
                new MemoryStream(),
                1_024,
                1_073_741_824,
                128,
                128),
            CancellationToken.None);

        result.OutputLimitExceeded.Should().BeTrue();
        result.StandardOutput.Length.Should().BeLessThanOrEqualTo(128);
        result.StandardError.Length.Should().BeLessThanOrEqualTo(128);
    }

    [Fact]
    public async Task ProcessRunner_Should_kill_worker_when_output_limit_is_exceeded()
    {
        var startInfo = new ProcessStartInfo(FindPythonExecutable());
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(
            "import sys\nwhile True:\n sys.stdout.write('x' * 4096)\n sys.stdout.flush()");
        ConfigureRedirects(startInfo);
        var runner = new SystemFinancialDocumentProcessRunner();
        using var safetyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stopwatch = Stopwatch.StartNew();

        var result = await runner.RunAsync(
            new FinancialDocumentProcessRequest(
                startInfo,
                new MemoryStream(),
                1_024,
                1_073_741_824,
                128,
                128),
            safetyTimeout.Token);

        result.OutputLimitExceeded.Should().BeTrue();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void ResolvePythonExecutable_Should_support_windows_and_unix_virtual_environments()
    {
        IsolatedFinancialDocumentMarkdownConverter.ResolvePythonExecutable("/python-home", true)
            .Should().Be(Path.Combine("/python-home", ".venv", "Scripts", "python.exe"));
        IsolatedFinancialDocumentMarkdownConverter.ResolvePythonExecutable("/python-home", false)
            .Should().Be(Path.Combine("/python-home", ".venv", "bin", "python"));
    }

    [Fact]
    public void GetMaximumStandardOutputBytes_Should_cover_worst_case_json_escaping()
    {
        var converter = CreateConverter(new FakeProcessRunner(
            new FinancialDocumentProcessResult(0, SuccessResponse, "", false)));

        converter.GetMaximumStandardOutputBytes(200_000)
            .Should().BeGreaterThanOrEqualTo(1_265_536);
    }

    private static IsolatedFinancialDocumentMarkdownConverter CreateConverter(
        IFinancialDocumentProcessRunner runner)
    {
        return new IsolatedFinancialDocumentMarkdownConverter(
            "python",
            "document_markdown.py",
            runner,
            maxStandardInputBytes: 20_971_520,
            maxWorkerMemoryBytes: 1_073_741_824,
            maxStandardOutputBytes: 1_000_000,
            maxStandardErrorBytes: 16_384);
    }

    private static string FindPythonExecutable()
    {
        var configuredHome = Environment.GetEnvironmentVariable("ORCHESTRATION_TEST_PYTHON_HOME");
        var starts = new[] { configuredHome, Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
            .Where(path => !string.IsNullOrWhiteSpace(path));

        foreach (var start in starts)
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start!));

            while (directory is not null)
            {
                foreach (var home in new[]
                         {
                             directory.FullName,
                             Path.Combine(directory.FullName, "python-agents"),
                             Path.Combine(directory.FullName, "python-agents", "data_agent"),
                             Path.Combine(directory.FullName, "data_agent")
                         })
                {
                    var executable = IsolatedFinancialDocumentMarkdownConverter.ResolvePythonExecutable(
                        home,
                        OperatingSystem.IsWindows());

                    if (File.Exists(executable))
                    {
                        return executable;
                    }
                }

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException("Managed Python executable was not found.");
    }

    private static string FindPythonHome()
    {
        var executable = new FileInfo(FindPythonExecutable());
        return executable.Directory!.Parent!.Parent!.FullName;
    }

    private static byte[] CreateMinimalPdf(string text)
    {
        var content = $"BT /F1 12 Tf 72 720 Td ({text}) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] "
                + "/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        using var document = new MemoryStream();
        var offsets = new List<long> { 0 };

        WriteAscii(document, "%PDF-1.4\n");
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(document.Position);
            WriteAscii(document, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var xrefOffset = document.Position;
        WriteAscii(document, $"xref\n0 {objects.Length + 1}\n");
        WriteAscii(document, "0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            WriteAscii(document, $"{offset:0000000000} 00000 n \n");
        }

        WriteAscii(
            document,
            $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\n"
                + $"startxref\n{xrefOffset}\n%%EOF\n");
        return document.ToArray();
    }

    private static void WriteAscii(Stream stream, string value)
    {
        stream.Write(Encoding.ASCII.GetBytes(value));
    }

    private static void ConfigureRedirects(ProcessStartInfo startInfo)
    {
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;
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

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout)
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

    private sealed class FakeProcessRunner : IFinancialDocumentProcessRunner
    {
        private readonly Func<FinancialDocumentProcessRequest, CancellationToken,
            Task<FinancialDocumentProcessResult>> _run;

        public FakeProcessRunner(FinancialDocumentProcessResult result)
            : this((_, _) => Task.FromResult(result))
        {
        }

        public FakeProcessRunner(
            Func<FinancialDocumentProcessRequest, CancellationToken,
                Task<FinancialDocumentProcessResult>> run)
        {
            _run = run;
        }

        public ProcessStartInfo? Request { get; private set; }
        public byte[] StandardInput { get; private set; } = [];

        public async Task<FinancialDocumentProcessResult> RunAsync(
            FinancialDocumentProcessRequest request,
            CancellationToken cancellationToken)
        {
            Request = request.StartInfo;
            using var buffer = new MemoryStream();
            await request.StandardInput.CopyToAsync(buffer, cancellationToken);
            StandardInput = buffer.ToArray();
            request.StandardInput.Position = 0;
            return await _run(request, cancellationToken);
        }
    }
}
