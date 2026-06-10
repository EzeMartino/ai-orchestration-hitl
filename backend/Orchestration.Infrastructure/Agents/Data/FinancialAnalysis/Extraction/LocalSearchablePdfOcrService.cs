using System.Text.Json;
using CSnakes.Runtime;
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
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly ILocalPdfToolRunner _runner;
    private readonly ISearchablePdfMerger _merger;

    public LocalSearchablePdfOcrService(IPythonEnvironment pythonEnvironment)
        : this(
            new LocalPdfToolRunner(),
            new CSnakesSearchablePdfMerger(pythonEnvironment))
    {
    }

    internal LocalSearchablePdfOcrService(
        ILocalPdfToolRunner runner,
        ISearchablePdfMerger merger)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(merger);

        _runner = runner;
        _merger = merger;
    }

    public async Task<SearchablePdfOcrResult> CreateSearchablePdfAsync(
        Stream pdf,
        StructuredFinancialMetricsPdfExtractionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "ai-orchestration-hitl-searchable-pdf-ocr",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(workingDirectory);

        try
        {
            var inputPath = Path.Combine(workingDirectory, "input.pdf");
            try
            {
                await using var output = File.Create(inputPath);
                await pdf.CopyToAsync(output, cancellationToken);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Failure(ToolFailure);
            }

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

            var outputPath = Path.Combine(workingDirectory, "searchable.pdf");
            var mergeResult = MergePages(
                workingDirectory,
                pagePaths,
                outputPath,
                cancellationToken);
            if (!mergeResult.Succeeded)
            {
                return Failure(NormalizeMergeFailure(mergeResult.FailureReason));
            }

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
        finally
        {
            TryDeleteDirectory(workingDirectory);
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
            cancellationToken);

        var imagePaths = Directory
            .EnumerateFiles(workingDirectory, "page-*.png")
            .OrderBy(GetGeneratedPageSortKey)
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
                cancellationToken);

            pagePaths.Add(outputPrefix + ".pdf");
        }

        return pagePaths;
    }

    private SearchablePdfMergeResponse MergePages(
        string workingDirectory,
        IReadOnlyList<string> pagePaths,
        string outputPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var requestJson = JsonSerializer.Serialize(
                new SearchablePdfMergeRequest(
                    workingDirectory,
                    pagePaths,
                    outputPath),
                JsonOptions);
            var responseJson = _merger.MergePdfPages(requestJson);
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
            _ => ToolFailure
        };
    }

    private static string NormalizeMergeFailure(string? failureReason)
    {
        return failureReason switch
        {
            MergeFailure => MergeFailure,
            PathOutsideWorkingDirectoryFailure => PathOutsideWorkingDirectoryFailure,
            _ => MergeFailure
        };
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
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
        return Math.Max(1, options.MaxPages);
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
        string OutputPath);

    private sealed record SearchablePdfMergeResponse(
        bool Succeeded,
        string? FailureReason);
}

internal interface ISearchablePdfMerger
{
    string MergePdfPages(string requestJson);
}

internal sealed class CSnakesSearchablePdfMerger(
    IPythonEnvironment pythonEnvironment) : ISearchablePdfMerger
{
    private readonly IPythonEnvironment _pythonEnvironment = pythonEnvironment;

    public string MergePdfPages(string requestJson)
    {
        return _pythonEnvironment
            .SearchablePdf()
            .MergePdfPages(requestJson);
    }
}
