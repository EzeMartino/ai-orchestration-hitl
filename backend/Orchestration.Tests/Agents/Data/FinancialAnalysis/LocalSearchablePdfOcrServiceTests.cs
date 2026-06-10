using System.Text.Json;
using FluentAssertions;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Extraction;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;

namespace Orchestration.Tests.Agents.Data.FinancialAnalysis;

public sealed class LocalSearchablePdfOcrServiceTests
{
    private static readonly byte[] SearchablePdfBytes = "%PDF-searchable"u8.ToArray();

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
            "page-1-ocr.pdf",
            "page-2-ocr.pdf");
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

    private static StructuredFinancialMetricsPdfExtractionOptions TestOptions()
    {
        return new StructuredFinancialMetricsPdfExtractionOptions
        {
            MaxPages = 2,
            OcrDpi = 150,
            OcrTimeoutSeconds = 5,
            PdfToPpmPath = "pdftoppm",
            TesseractPath = "tesseract",
            TesseractLanguage = "eng"
        };
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
                    arguments[^1] + "-1.png",
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

        public FakeSearchablePdfMerger(
            string responseJson = """
                {
                  "succeeded": true,
                  "failureReason": null
                }
                """,
            bool createOutput = true)
        {
            _responseJson = responseJson;
            _createOutput = createOutput;
        }

        public IReadOnlyList<string> PageNames { get; private set; } = [];

        public string MergePdfPages(string requestJson)
        {
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

            return _responseJson;
        }
    }
}
