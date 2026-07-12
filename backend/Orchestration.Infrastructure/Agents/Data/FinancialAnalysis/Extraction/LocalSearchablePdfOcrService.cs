using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Orchestration.Application.Agents.Data.FinancialAnalysis;
using Orchestration.Application.Agents.Data.FinancialAnalysis.Extraction;
using Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Extraction;

public sealed class LocalSearchablePdfOcrService : ISearchablePdfOcrService
{
    private const string DependencyFailure = "dependency_not_configured";
    private const string ToolFailure = "tool_failed";
    private const string TimeoutFailure = "timeout";
    private const string MergeFailure = "merge_failed";
    private const string PathOutsideWorkingDirectoryFailure =
        "path_outside_working_directory";
    private const string OutputReadFailure = "output_read_failed";
    private const string ResourceLimitFailure = "resource_limit_exceeded";
    private const string WorkingDirectoryPrefix =
        "ai-orchestration-hitl-searchable-pdf-ocr-";
    private const int CleanupAttempts = 3;
    private static readonly TimeSpan CleanupRetryDelay =
        TimeSpan.FromMilliseconds(50);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly ILocalPdfToolRunner _runner;
    private readonly ISearchablePdfMerger _merger;
    private readonly Func<string> _createdWorkingDirectoryFactory;

    public LocalSearchablePdfOcrService(
        string pythonHome,
        long maxWorkerMemoryBytes)
        : this(
            new LocalPdfToolRunner(),
            new IsolatedSearchablePdfMerger(
                pythonHome,
                maxWorkerMemoryBytes),
            CreateWorkingDirectory)
    {
    }

    internal LocalSearchablePdfOcrService(
        ILocalPdfToolRunner runner,
        ISearchablePdfMerger merger)
        : this(runner, merger, CreateWorkingDirectory)
    {
    }

    internal LocalSearchablePdfOcrService(
        ILocalPdfToolRunner runner,
        ISearchablePdfMerger merger,
        Func<string> createdWorkingDirectoryFactory)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(merger);
        ArgumentNullException.ThrowIfNull(createdWorkingDirectoryFactory);

        _runner = runner;
        _merger = merger;
        _createdWorkingDirectoryFactory = createdWorkingDirectoryFactory;
    }

    public async Task<SearchablePdfOcrResult> CreateSearchablePdfAsync(
        Stream pdf,
        StructuredFinancialMetricsPdfExtractionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (options.MaxPages <= 0)
        {
            return Failure(ToolFailure);
        }

        string? workingDirectory = null;

        try
        {
            workingDirectory = _createdWorkingDirectoryFactory();

            var inputPath = Path.Combine(workingDirectory, "input.pdf");
            try
            {
                await CopyInputWithinTemporaryLimitAsync(
                    pdf,
                    inputPath,
                    options,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ResourceLimitExceededException)
            {
                throw;
            }
            catch (Exception)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Failure(ToolFailure);
            }

            EnsureWithinTemporaryLimit(workingDirectory, options);

            IReadOnlyList<string> pagePaths;
            try
            {
                pagePaths = await CreateOcrPagePdfsAsync(
                    inputPath,
                    workingDirectory,
                    options,
                    cancellationToken);
            }
            catch (LocalPdfToolException exception)
            {
                return Failure(MapToolFailure(exception.Failure));
            }

            if (pagePaths.Count == 0)
            {
                return Failure(ToolFailure);
            }

            EnsureWithinTemporaryLimit(workingDirectory, options);

            var outputPath = Path.Combine(workingDirectory, "searchable.pdf");
            cancellationToken.ThrowIfCancellationRequested();
            var mergeResult = await MergePagesAsync(
                workingDirectory,
                pagePaths,
                outputPath,
                options,
                cancellationToken);
            if (!mergeResult.Succeeded)
            {
                return Failure(NormalizeMergeFailure(mergeResult.FailureReason));
            }

            EnsureWithinTemporaryLimit(workingDirectory, options);
            EnsureSearchablePdfWithinLimit(outputPath, options);

            try
            {
                var pdfBytes = await File.ReadAllBytesAsync(
                    outputPath,
                    cancellationToken);

                return pdfBytes.Length == 0
                    ? Failure(OutputReadFailure)
                    : new SearchablePdfOcrResult(true, pdfBytes, null);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Failure(OutputReadFailure);
            }
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ResourceLimitExceededException)
        {
            return Failure(ResourceLimitFailure);
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure(ToolFailure);
        }
        finally
        {
            if (workingDirectory is not null)
            {
                await TryDeleteDirectoryAsync(workingDirectory);
            }
        }
    }

    private async Task<IReadOnlyList<string>> CreateOcrPagePdfsAsync(
        string inputPath,
        string workingDirectory,
        StructuredFinancialMetricsPdfExtractionOptions options,
        CancellationToken cancellationToken)
    {
        var imagePrefix = Path.Combine(workingDirectory, "page");
        await _runner.RunAsync(
            options.PdfToPpmPath,
            [
                "-r",
                GetDpi(options).ToString(),
                "-png",
                "-f",
                "1",
                "-l",
                GetMaxPages(options).ToString(),
                inputPath,
                imagePrefix
            ],
            GetTimeout(options),
            cancellationToken,
            GetMaximumTemporaryBytes(options),
            workingDirectory,
            GetMaximumToolOutputBytes(options));

        EnsureWithinTemporaryLimit(workingDirectory, options);

        var imagePaths = Directory
            .EnumerateFiles(workingDirectory, "page-*.png")
            .OrderBy(GetGeneratedPageSortKey)
            .Take(GetMaxPages(options))
            .ToArray();
        var pagePaths = new List<string>(imagePaths.Length);

        foreach (var imagePath in imagePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outputPrefix = Path.Combine(
                workingDirectory,
                $"{Path.GetFileNameWithoutExtension(imagePath)}-ocr");
            await _runner.RunAsync(
                options.TesseractPath,
                [
                    imagePath,
                    outputPrefix,
                    "-l",
                    GetTesseractLanguage(options),
                    "pdf"
                ],
                GetTimeout(options),
                cancellationToken,
                GetMaximumTemporaryBytes(options),
                workingDirectory,
                GetMaximumToolOutputBytes(options));

            EnsureWithinTemporaryLimit(workingDirectory, options);
            pagePaths.Add(outputPrefix + ".pdf");
        }

        EnsureWithinTemporaryLimit(workingDirectory, options);
        return pagePaths;
    }

    private async Task<SearchablePdfMergeResponse> MergePagesAsync(
        string workingDirectory,
        IReadOnlyList<string> pagePaths,
        string outputPath,
        StructuredFinancialMetricsPdfExtractionOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var requestJson = JsonSerializer.Serialize(
                new SearchablePdfMergeRequest(
                    workingDirectory,
                    pagePaths,
                    outputPath,
                    GetMaximumTemporaryBytes(options),
                    GetMaximumSearchablePdfBytes(options)),
                JsonOptions);
            var responseJson = await _merger.MergePdfPagesAsync(
                requestJson,
                GetTimeout(options),
                GetMaximumMergerOutputBytes(options),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var response = JsonSerializer.Deserialize<SearchablePdfMergeResponse>(
                responseJson,
                JsonOptions);

            return response
                ?? new SearchablePdfMergeResponse(false, MergeFailure);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new SearchablePdfMergeResponse(false, MergeFailure);
        }
    }

    private static SearchablePdfOcrResult Failure(string failureReason)
    {
        return new SearchablePdfOcrResult(false, [], failureReason);
    }

    private static string MapToolFailure(LocalPdfToolFailure failure)
    {
        return failure switch
        {
            LocalPdfToolFailure.DependencyMissing => DependencyFailure,
            LocalPdfToolFailure.ToolFailed => ToolFailure,
            LocalPdfToolFailure.Timeout => TimeoutFailure,
            LocalPdfToolFailure.ResourceLimitExceeded => ResourceLimitFailure,
            _ => ToolFailure
        };
    }

    private static string NormalizeMergeFailure(string? failureReason)
    {
        return failureReason switch
        {
            MergeFailure => MergeFailure,
            TimeoutFailure => TimeoutFailure,
            PathOutsideWorkingDirectoryFailure => PathOutsideWorkingDirectoryFailure,
            ResourceLimitFailure => ResourceLimitFailure,
            _ => MergeFailure
        };
    }

    private static string CreateWorkingDirectory()
    {
        return Directory.CreateTempSubdirectory(WorkingDirectoryPrefix).FullName;
    }

    private static void EnsureWithinTemporaryLimit(
        string workingDirectory,
        StructuredFinancialMetricsPdfExtractionOptions options)
    {
        var limit = GetMaximumTemporaryBytes(options);
        long totalBytes = 0;

        foreach (var filePath in Directory.EnumerateFiles(
                     workingDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            var fileLength = new FileInfo(filePath).Length;
            if (fileLength > limit - totalBytes)
            {
                throw new ResourceLimitExceededException();
            }

            totalBytes += fileLength;
        }
    }

    private static void EnsureSearchablePdfWithinLimit(
        string outputPath,
        StructuredFinancialMetricsPdfExtractionOptions options)
    {
        if (!File.Exists(outputPath))
        {
            return;
        }

        var limit = GetMaximumSearchablePdfBytes(options);
        if (new FileInfo(outputPath).Length > limit)
        {
            throw new ResourceLimitExceededException();
        }
    }

    private static async Task CopyInputWithinTemporaryLimitAsync(
        Stream input,
        string inputPath,
        StructuredFinancialMetricsPdfExtractionOptions options,
        CancellationToken cancellationToken)
    {
        const int bufferSize = 81_920;
        var limit = GetMaximumTemporaryBytes(options);
        var buffer = new byte[bufferSize];
        long writtenBytes = 0;

        await using var output = new FileStream(
            inputPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize,
            FileOptions.Asynchronous);

        while (true)
        {
            var read = await input.ReadAsync(
                buffer.AsMemory(),
                cancellationToken);
            if (read == 0)
            {
                return;
            }

            if (read > limit - writtenBytes)
            {
                throw new ResourceLimitExceededException();
            }

            await output.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
            writtenBytes += read;
        }
    }

    private static long GetMaximumTemporaryBytes(
        StructuredFinancialMetricsPdfExtractionOptions options)
    {
        return Math.Max(0, options.MaxTemporaryBytes);
    }

    private static long GetMaximumSearchablePdfBytes(
        StructuredFinancialMetricsPdfExtractionOptions options)
    {
        return Math.Max(0, options.MaxSearchablePdfBytes);
    }

    private static long GetMaximumToolOutputBytes(
        StructuredFinancialMetricsPdfExtractionOptions options)
    {
        return Math.Max(0, options.MaxToolOutputBytes);
    }

    private static int GetMaximumMergerOutputBytes(
        StructuredFinancialMetricsPdfExtractionOptions options)
    {
        return (int)Math.Min(int.MaxValue, GetMaximumToolOutputBytes(options));
    }

    private static async Task TryDeleteDirectoryAsync(string directory)
    {
        for (var attempt = 0; attempt < CleanupAttempts; attempt++)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    return;
                }

                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (Exception exception)
            {
                Trace.TraceWarning(
                    "Financial PDF temporary cleanup failed for '{0}': {1}",
                    directory,
                    exception.Message);
                return;
            }

            TryClearReadOnlyAttributes(directory);

            if (attempt < CleanupAttempts - 1)
            {
                await Task.Delay(CleanupRetryDelay, CancellationToken.None);
            }
        }

        Trace.TraceWarning(
            "Financial PDF temporary cleanup was deferred for '{0}'.",
            directory);
    }

    private static void TryClearReadOnlyAttributes(string directory)
    {
        try
        {
            foreach (var filePath in Directory.EnumerateFiles(
                         directory,
                         "*",
                         SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(filePath);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(
                        filePath,
                        attributes & ~FileAttributes.ReadOnly);
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private static int GetGeneratedPageSortKey(string path)
    {
        return GetGeneratedPageNumber(path) ?? int.MaxValue;
    }

    private static int? GetGeneratedPageNumber(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var separatorIndex = fileName.LastIndexOf('-');

        return separatorIndex >= 0
            && int.TryParse(fileName[(separatorIndex + 1)..], out var pageNumber)
                ? pageNumber
                : null;
    }

    private static int GetDpi(StructuredFinancialMetricsPdfExtractionOptions options)
    {
        return Math.Max(1, options.OcrDpi);
    }

    private static int GetMaxPages(StructuredFinancialMetricsPdfExtractionOptions options)
    {
        return options.MaxPages;
    }

    private static TimeSpan GetTimeout(
        StructuredFinancialMetricsPdfExtractionOptions options)
    {
        return TimeSpan.FromSeconds(Math.Max(1, options.OcrTimeoutSeconds));
    }

    private static string GetTesseractLanguage(
        StructuredFinancialMetricsPdfExtractionOptions options)
    {
        return string.IsNullOrWhiteSpace(options.TesseractLanguage)
            ? "eng"
            : options.TesseractLanguage.Trim();
    }

    private sealed record SearchablePdfMergeRequest(
        string WorkingDirectory,
        IReadOnlyList<string> PagePaths,
        string OutputPath,
        long MaxTemporaryBytes,
        long MaxSearchablePdfBytes);

    private sealed record SearchablePdfMergeResponse(
        bool Succeeded,
        string? FailureReason);

    private sealed class ResourceLimitExceededException : Exception;
}

internal interface ISearchablePdfMerger
{
    Task<string> MergePdfPagesAsync(
        string requestJson,
        TimeSpan timeout,
        int maxStandardOutputBytes,
        CancellationToken cancellationToken);
}

internal sealed class IsolatedSearchablePdfMerger : ISearchablePdfMerger
{
    private const long DefaultMaxStandardInputBytes = 65_536;
    private const int DefaultMaxStandardErrorBytes = 16_384;

    private readonly string _pythonExecutable;
    private readonly string _scriptPath;
    private readonly IFinancialDocumentProcessRunner _processRunner;
    private readonly long _maxWorkerMemoryBytes;
    private readonly long _maxStandardInputBytes;
    private readonly int _maxStandardErrorBytes;

    public IsolatedSearchablePdfMerger(
        string pythonHome,
        long maxWorkerMemoryBytes)
        : this(
            IsolatedFinancialDocumentMarkdownConverter.ResolvePythonExecutable(
                pythonHome,
                OperatingSystem.IsWindows()),
            Path.Combine(pythonHome, "searchable_pdf.py"),
            new SystemFinancialDocumentProcessRunner(),
            maxWorkerMemoryBytes,
            DefaultMaxStandardInputBytes,
            DefaultMaxStandardErrorBytes)
    {
    }

    internal IsolatedSearchablePdfMerger(
        string pythonExecutable,
        string scriptPath,
        IFinancialDocumentProcessRunner processRunner,
        long maxWorkerMemoryBytes,
        long maxStandardInputBytes,
        int maxStandardErrorBytes)
    {
        _pythonExecutable = pythonExecutable;
        _scriptPath = scriptPath;
        _processRunner = processRunner;
        _maxWorkerMemoryBytes = Math.Max(1, maxWorkerMemoryBytes);
        _maxStandardInputBytes = Math.Max(1, maxStandardInputBytes);
        _maxStandardErrorBytes = Math.Max(1, maxStandardErrorBytes);
    }

    public async Task<string> MergePdfPagesAsync(
        string requestJson,
        TimeSpan timeout,
        int maxStandardOutputBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestJson);
        cancellationToken.ThrowIfCancellationRequested();

        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        using var standardInput = new MemoryStream(
            Encoding.UTF8.GetBytes(requestJson),
            writable: false);
        var startInfo = new ProcessStartInfo(_pythonExecutable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-I");
        startInfo.ArgumentList.Add(_scriptPath);
        startInfo.ArgumentList.Add("--max-memory-bytes");
        startInfo.ArgumentList.Add(
            _maxWorkerMemoryBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));

        try
        {
            var result = await _processRunner.RunAsync(
                new FinancialDocumentProcessRequest(
                    startInfo,
                    standardInput,
                    _maxStandardInputBytes,
                    _maxWorkerMemoryBytes,
                    Math.Max(1, maxStandardOutputBytes),
                    _maxStandardErrorBytes),
                linkedCancellation.Token);
            cancellationToken.ThrowIfCancellationRequested();

            return result.ExitCode == 0 && !result.OutputLimitExceeded
                ? result.StandardOutput
                : Failure(MergeFailure);
        }
        catch (OperationCanceledException) when (
            timeoutCancellation.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            return Failure(TimeoutFailure);
        }
    }

    private const string MergeFailure = "merge_failed";
    private const string TimeoutFailure = "timeout";

    private static string Failure(string failureReason)
    {
        return JsonSerializer.Serialize(new SearchablePdfMergeResponse(
            false,
            failureReason));
    }

    private sealed record SearchablePdfMergeResponse(
        bool Succeeded,
        string FailureReason);
}
