using System.Text.Json;
using FluentAssertions;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Extraction;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class LocalSearchablePdfOcrServiceTests
{
    private const string WorkingDirectoryPrefix =
        "ai-orchestration-hitl-searchable-pdf-ocr-";
    private static readonly byte[] SearchablePdfBytes = "%PDF-searchable"u8.ToArray();

    [Fact]
    public void Options_Should_define_resource_limit_defaults()
    {
        var options = new StructuredFinancialMetricsPdfExtractionOptions();

        options.MaxTemporaryBytes.Should().Be(536_870_912);
        options.MaxSearchablePdfBytes.Should().Be(104_857_600);
    }

    [Fact]
    public async Task CreateSearchablePdfAsync_Should_render_ocr_and_merge_pages_in_order()
    {
        var runner = new FakePdfToolRunner();
        var merger = new FakeSearchablePdfMerger();
        var service = new LocalSearchablePdfOcrService(runner, merger);

        await using var input = new MemoryStream("%PDF-test"u8.ToArray());
        var result = await service.CreateSearchablePdfAsync(
            input,
            TestOptions(),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.PdfBytes.Should().Equal(SearchablePdfBytes);
        result.FailureReason.Should().BeNull();
        runner.Commands.Should().HaveCount(3);

        var pdfToPpm = runner.Commands.Should()
            .ContainSingle(command => command.FileName == "pdftoppm")
            .Which;
        pdfToPpm.Arguments.Should().ContainInOrder(
            "-r",
            "150",
            "-png",
            "-f",
            "1",
            "-l",
            "2");

        var tesseractCommands = runner.Commands
            .Where(command => command.FileName == "tesseract")
            .ToArray();
        tesseractCommands.Should().HaveCount(2);
        tesseractCommands.Should().OnlyContain(
            command => command.Arguments[command.Arguments.Count - 1] == "pdf");
        merger.PageNames.Should().Equal(
            "page-2-ocr.pdf",
            "page-10-ocr.pdf");
        AssertTemporaryDirectoryDeleted(runner);
    }

    [Fact]
    public async Task CreateSearchablePdfAsync_Should_reject_nonpositive_max_pages_without_tool_work()
    {
        var factoryCalled = false;
        var runner = new FakePdfToolRunner();
        var merger = new FakeSearchablePdfMerger();
        var service = new LocalSearchablePdfOcrService(
            runner,
            merger,
            () =>
            {
                factoryCalled = true;
                return CreateTemporaryDirectory();
            });

        await using var input = new MemoryStream("%PDF-test"u8.ToArray());
        var result = await service.CreateSearchablePdfAsync(
            input,
            TestOptions(maxPages: 0),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Be("tool_failed");
        runner.Commands.Should().BeEmpty();
        merger.CallCount.Should().Be(0);
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public async Task CreateSearchablePdfAsync_Should_return_safe_failure_when_working_directory_creation_fails()
    {
        var runner = new FakePdfToolRunner();
        var merger = new FakeSearchablePdfMerger();
        var service = new LocalSearchablePdfOcrService(
            runner,
            merger,
            () => throw new IOException(
                @"Unable to create C:\sensitive\searchable-pdf-workspace"));

        await using var input = new MemoryStream("%PDF-test"u8.ToArray());
        var result = await service.CreateSearchablePdfAsync(
            input,
            TestOptions(),
            CancellationToken.None);

        result.Should().BeEquivalentTo(new
        {
            Succeeded = false,
            PdfBytes = Array.Empty<byte>(),
            FailureReason = "tool_failed"
        });
        runner.Commands.Should().BeEmpty();
        merger.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CreateSearchablePdfAsync_Should_create_unique_private_production_working_directories()
    {
        var workingDirectories = new List<string>();
        var existedDuringRun = new List<bool>();
        var unixModes = new List<UnixFileMode>();
        var runner = new FakePdfToolRunner(
            (_, arguments, _) =>
            {
                var workingDirectory = Path.GetDirectoryName(arguments[^2])!;
                workingDirectories.Add(workingDirectory);
                existedDuringRun.Add(Directory.Exists(workingDirectory));
                if (!OperatingSystem.IsWindows())
                {
                    unixModes.Add(File.GetUnixFileMode(workingDirectory));
                }

                throw new LocalPdfToolException(LocalPdfToolFailure.ToolFailed);
            });
        var service = new LocalSearchablePdfOcrService(
            runner,
            new FakeSearchablePdfMerger());

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var input = new MemoryStream("%PDF-test"u8.ToArray());
            var result = await service.CreateSearchablePdfAsync(
                input,
                TestOptions(),
                CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.FailureReason.Should().Be("tool_failed");
        }

        workingDirectories.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        existedDuringRun.Should().OnlyContain(existed => existed);
        workingDirectories.Should().OnlyContain(
            directory => Path.GetFileName(directory).StartsWith(
                WorkingDirectoryPrefix,
                StringComparison.Ordinal));
        workingDirectories.Should().OnlyContain(
            directory => Path.TrimEndingDirectorySeparator(
                    new DirectoryInfo(directory).Parent!.FullName)
                == Path.TrimEndingDirectorySeparator(
                    new DirectoryInfo(Path.GetTempPath()).FullName));
        workingDirectories.Should().OnlyContain(
            directory => !Directory.Exists(directory));

        if (!OperatingSystem.IsWindows())
        {
            const UnixFileMode groupOrOtherPermissions =
                UnixFileMode.GroupRead
                | UnixFileMode.GroupWrite
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherWrite
                | UnixFileMode.OtherExecute;
            unixModes.Should().OnlyContain(
                mode => (mode & groupOrOtherPermissions) == 0);
        }
    }

    [Fact]
    public async Task CreateSearchablePdfAsync_Should_limit_input_temporary_bytes_and_cleanup()
    {
        string? workingDirectory = null;
        var runner = new FakePdfToolRunner();
        var merger = new FakeSearchablePdfMerger();
        var service = new LocalSearchablePdfOcrService(
            runner,
            merger,
            () => workingDirectory = CreateTemporaryDirectory());

        await using var input = new MemoryStream("%PDF-test"u8.ToArray());
        var result = await service.CreateSearchablePdfAsync(
            input,
            TestOptions(maxTemporaryBytes: 0),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Be("resource_limit_exceeded");
        runner.Commands.Should().BeEmpty();
        merger.CallCount.Should().Be(0);
        workingDirectory.Should().NotBeNull();
        Directory.Exists(workingDirectory!).Should().BeFalse();
    }

    [Fact]
    public async Task CreateSearchablePdfAsync_Should_limit_rendered_temporary_bytes_and_cleanup()
    {
        var runner = new FakePdfToolRunner();
        var merger = new FakeSearchablePdfMerger();
        var service = new LocalSearchablePdfOcrService(runner, merger);

        await using var input = new MemoryStream("%PDF-test"u8.ToArray());
        var result = await service.CreateSearchablePdfAsync(
            input,
            TestOptions(maxTemporaryBytes: 10),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Be("resource_limit_exceeded");
        runner.Commands.Should().ContainSingle(
            command => command.FileName == "pdftoppm");
        merger.CallCount.Should().Be(0);
        AssertTemporaryDirectoryDeleted(runner);
    }

    [Fact]
    public async Task CreateSearchablePdfAsync_Should_limit_ocr_temporary_bytes_and_cleanup()
    {
        var runner = new FakePdfToolRunner();
        var merger = new FakeSearchablePdfMerger();
        var service = new LocalSearchablePdfOcrService(runner, merger);

        await using var input = new MemoryStream("%PDF-test"u8.ToArray());
        var result = await service.CreateSearchablePdfAsync(
            input,
            TestOptions(maxTemporaryBytes: 19),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Be("resource_limit_exceeded");
        runner.Commands.Should().HaveCount(2);
        runner.Commands[1].FileName.Should().Be("tesseract");
        merger.CallCount.Should().Be(0);
        AssertTemporaryDirectoryDeleted(runner);
    }

    [Fact]
    public async Task CreateSearchablePdfAsync_Should_limit_merged_pdf_before_read_and_cleanup()
    {
        var runner = new FakePdfToolRunner();
        var merger = new FakeSearchablePdfMerger();
        var service = new LocalSearchablePdfOcrService(runner, merger);

        await using var input = new MemoryStream("%PDF-test"u8.ToArray());
        var result = await service.CreateSearchablePdfAsync(
            input,
            TestOptions(maxSearchablePdfBytes: 1),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Be("resource_limit_exceeded");
        merger.CallCount.Should().Be(1);
        AssertTemporaryDirectoryDeleted(runner);
    }

    [Fact]
    public async Task CreateSearchablePdfAsync_Should_limit_cumulative_bytes_after_merge()
    {
        var runner = new FakePdfToolRunner();
        var merger = new FakeSearchablePdfMerger();
        var service = new LocalSearchablePdfOcrService(runner, merger);

        await using var input = new MemoryStream("%PDF-test"u8.ToArray());
        var result = await service.CreateSearchablePdfAsync(
            input,
            TestOptions(maxTemporaryBytes: 29),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Be("resource_limit_exceeded");
        merger.CallCount.Should().Be(1);
        AssertTemporaryDirectoryDeleted(runner);
    }

    [Theory]
    [InlineData("DependencyMissing", "dependency_not_configured")]
    [InlineData("ToolFailed", "tool_failed")]
    [InlineData("Timeout", "timeout")]
    public async Task CreateSearchablePdfAsync_Should_map_tool_failures_and_cleanup(
        string failureName,
        string expectedFailureReason)
    {
        var failure = Enum.Parse<LocalPdfToolFailure>(failureName);
        var runner = new FakePdfToolRunner(
            (_, _, _) => Task.FromException(new LocalPdfToolException(failure)));
        var service = new LocalSearchablePdfOcrService(
            runner,
            new FakeSearchablePdfMerger());

        await using var input = new MemoryStream("%PDF-test"u8.ToArray());
        var result = await service.CreateSearchablePdfAsync(
            input,
            TestOptions(),
            CancellationToken.None);

        result.Should().BeEquivalentTo(new
        {
            Succeeded = false,
            PdfBytes = Array.Empty<byte>(),
            FailureReason = expectedFailureReason
        });
        AssertTemporaryDirectoryDeleted(runner);
    }

    [Fact]
    public async Task CreateSearchablePdfAsync_Should_return_safe_merge_failure_and_cleanup()
    {
        var runner = new FakePdfToolRunner();
        var merger = new FakeSearchablePdfMerger(
            responseJson: """
                {
                  "succeeded": false,
                  "failureReason": "merge_failed"
                }
                """,
            createOutput: false);
        var service = new LocalSearchablePdfOcrService(runner, merger);

        await using var input = new MemoryStream("%PDF-test"u8.ToArray());
        var result = await service.CreateSearchablePdfAsync(
            input,
            TestOptions(),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.PdfBytes.Should().BeEmpty();
        result.FailureReason.Should().Be("merge_failed");
        AssertTemporaryDirectoryDeleted(runner);
    }

    [Fact]
    public async Task CreateSearchablePdfAsync_Should_not_expose_unsafe_merge_failure_details()
    {
        var runner = new FakePdfToolRunner();
        var merger = new FakeSearchablePdfMerger(
            responseJson: """
                {
                  "succeeded": false,
                  "failureReason": "C:\\sensitive\\document.pdf"
                }
                """,
            createOutput: false);
        var service = new LocalSearchablePdfOcrService(runner, merger);

        await using var input = new MemoryStream("%PDF-test"u8.ToArray());
        var result = await service.CreateSearchablePdfAsync(
            input,
            TestOptions(),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Be("merge_failed");
        AssertTemporaryDirectoryDeleted(runner);
    }

    [Fact]
    public async Task CreateSearchablePdfAsync_Should_return_safe_read_failure_and_cleanup()
    {
        var runner = new FakePdfToolRunner();
        var merger = new FakeSearchablePdfMerger(createOutput: false);
        var service = new LocalSearchablePdfOcrService(runner, merger);

        await using var input = new MemoryStream("%PDF-test"u8.ToArray());
        var result = await service.CreateSearchablePdfAsync(
            input,
            TestOptions(),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.PdfBytes.Should().BeEmpty();
        result.FailureReason.Should().Be("output_read_failed");
        AssertTemporaryDirectoryDeleted(runner);
    }

    [Fact]
    public async Task CreateSearchablePdfAsync_Should_preserve_caller_cancellation_and_cleanup()
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new FakePdfToolRunner(
            (_, _, _) =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            });
        var service = new LocalSearchablePdfOcrService(
            runner,
            new FakeSearchablePdfMerger());

        await using var input = new MemoryStream("%PDF-test"u8.ToArray());
        var action = () => service.CreateSearchablePdfAsync(
            input,
            TestOptions(),
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        AssertTemporaryDirectoryDeleted(runner);
    }

    [Fact]
    public async Task CreateSearchablePdfAsync_Should_cancel_after_final_tesseract_before_merge_and_cleanup()
    {
        using var cancellation = new CancellationTokenSource();
        var tesseractCallCount = 0;
        var runner = new FakePdfToolRunner(
            async (fileName, arguments, cancellationToken) =>
            {
                if (fileName == "pdftoppm")
                {
                    await File.WriteAllBytesAsync(
                        arguments[^1] + "-10.png",
                        [1],
                        cancellationToken);
                    await File.WriteAllBytesAsync(
                        arguments[^1] + "-2.png",
                        [2],
                        cancellationToken);
                    return;
                }

                await File.WriteAllBytesAsync(
                    arguments[1] + ".pdf",
                    "%PDF-page"u8.ToArray(),
                    cancellationToken);
                tesseractCallCount++;
                if (tesseractCallCount == 2)
                {
                    cancellation.Cancel();
                }
            });
        var merger = new FakeSearchablePdfMerger();
        var service = new LocalSearchablePdfOcrService(runner, merger);

        await using var input = new MemoryStream("%PDF-test"u8.ToArray());
        var action = () => service.CreateSearchablePdfAsync(
            input,
            TestOptions(),
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        runner.Commands.Should().HaveCount(3);
        merger.CallCount.Should().Be(0);
        AssertTemporaryDirectoryDeleted(runner);
    }

    [Fact]
    public async Task CreateSearchablePdfAsync_Should_retry_cleanup_for_read_only_files()
    {
        var runner = new FakePdfToolRunner();
        var merger = new FakeSearchablePdfMerger(
            responseJson: """
                {
                  "succeeded": false,
                  "failureReason": "merge_failed"
                }
                """,
            createOutput: false,
            createReadOnlyFile: true);
        var service = new LocalSearchablePdfOcrService(runner, merger);

        await using var input = new MemoryStream("%PDF-test"u8.ToArray());
        var result = await service.CreateSearchablePdfAsync(
            input,
            TestOptions(),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Be("merge_failed");
        AssertTemporaryDirectoryDeleted(runner);
    }

    private static StructuredFinancialMetricsPdfExtractionOptions TestOptions(
        int maxPages = 2,
        long maxTemporaryBytes = 536_870_912,
        long maxSearchablePdfBytes = 104_857_600)
    {
        return new StructuredFinancialMetricsPdfExtractionOptions
        {
            MaxPages = maxPages,
            OcrDpi = 150,
            OcrTimeoutSeconds = 5,
            PdfToPpmPath = "pdftoppm",
            TesseractPath = "tesseract",
            TesseractLanguage = "eng",
            MaxTemporaryBytes = maxTemporaryBytes,
            MaxSearchablePdfBytes = maxSearchablePdfBytes
        };
    }

    private static string CreateTemporaryDirectory()
    {
        return Directory.CreateTempSubdirectory(
            "ai-orchestration-hitl-searchable-pdf-ocr-tests-").FullName;
    }

    private static void AssertTemporaryDirectoryDeleted(FakePdfToolRunner runner)
    {
        runner.WorkingDirectory.Should().NotBeNull();
        Directory.Exists(runner.WorkingDirectory!).Should().BeFalse();
    }

    private sealed record RecordedCommand(
        string FileName,
        IReadOnlyList<string> Arguments,
        TimeSpan Timeout);

    private sealed class FakePdfToolRunner : ILocalPdfToolRunner
    {
        private readonly Func<
            string,
            IReadOnlyList<string>,
            CancellationToken,
            Task>? _run;

        public FakePdfToolRunner(
            Func<string, IReadOnlyList<string>, CancellationToken, Task>? run = null)
        {
            _run = run;
        }

        public List<RecordedCommand> Commands { get; } = [];

        public string? WorkingDirectory { get; private set; }

        public async Task RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Commands.Add(new RecordedCommand(fileName, arguments, timeout));
            WorkingDirectory ??= Path.GetDirectoryName(
                fileName == "pdftoppm"
                    ? arguments[^2]
                    : arguments[0]);

            if (_run is not null)
            {
                await _run(fileName, arguments, cancellationToken);
                return;
            }

            if (fileName == "pdftoppm")
            {
                await File.WriteAllBytesAsync(
                    arguments[^1] + "-10.png",
                    [1],
                    cancellationToken);
                await File.WriteAllBytesAsync(
                    arguments[^1] + "-2.png",
                    [2],
                    cancellationToken);
                return;
            }

            await File.WriteAllBytesAsync(
                arguments[1] + ".pdf",
                "%PDF-page"u8.ToArray(),
                cancellationToken);
        }
    }

    private sealed class FakeSearchablePdfMerger : ISearchablePdfMerger
    {
        private readonly string _responseJson;
        private readonly bool _createOutput;
        private readonly bool _createReadOnlyFile;

        public FakeSearchablePdfMerger(
            string responseJson = """
                {
                  "succeeded": true,
                  "failureReason": null
                }
                """,
            bool createOutput = true,
            bool createReadOnlyFile = false)
        {
            _responseJson = responseJson;
            _createOutput = createOutput;
            _createReadOnlyFile = createReadOnlyFile;
        }

        public IReadOnlyList<string> PageNames { get; private set; } = [];

        public int CallCount { get; private set; }

        public string MergePdfPages(string requestJson)
        {
            CallCount++;
            using var request = JsonDocument.Parse(requestJson);
            PageNames = request.RootElement
                .GetProperty("pagePaths")
                .EnumerateArray()
                .Select(path => Path.GetFileName(path.GetString()))
                .ToArray()!;

            if (_createOutput)
            {
                var outputPath = request.RootElement
                    .GetProperty("outputPath")
                    .GetString()!;
                File.WriteAllBytes(outputPath, SearchablePdfBytes);
            }

            if (_createReadOnlyFile)
            {
                var workingDirectory = request.RootElement
                    .GetProperty("workingDirectory")
                    .GetString()!;
                var readOnlyPath = Path.Combine(
                    workingDirectory,
                    "read-only.tmp");
                File.WriteAllText(readOnlyPath, "temporary");
                File.SetAttributes(readOnlyPath, FileAttributes.ReadOnly);
            }

            return _responseJson;
        }
    }
}
