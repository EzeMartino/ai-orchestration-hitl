using System.ComponentModel;
using System.Diagnostics;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Infrastructure.Agents.Data.FinancialAnalysis.Pdf;

public sealed class LocalOcrTextExtractor : IOcrTextExtractor
{
    private const string DependencyMessage = "PDF OCR dependencies are not configured.";
    private const string FailedMessage = "PDF OCR processing failed.";
    private const string TimeoutMessage = "PDF OCR processing timed out.";

    public async Task<IReadOnlyList<StructuredFinancialMetricsExtractedPage>> ExtractTextAsync(
        Stream pdf,
        StructuredFinancialMetricsPdfExtractionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (options.MaxPages <= 0)
        {
            return [];
        }

        var workDirectory = Path.Combine(
            Path.GetTempPath(),
            "ai-orchestration-hitl-pdf-ocr",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(workDirectory);

        try
        {
            var pdfPath = Path.Combine(workDirectory, "input.pdf");
            await using (var output = File.Create(pdfPath))
            {
                await pdf.CopyToAsync(output, cancellationToken);
            }

            var outputPrefix = Path.Combine(workDirectory, "page");
            await RunProcessAsync(
                options.PdfToPpmPath,
                [
                    "-r",
                    GetDpi(options).ToString(),
                    "-png",
                    "-f",
                    "1",
                    "-l",
                    GetMaxPages(options).ToString(),
                    pdfPath,
                    outputPrefix
                ],
                GetTimeout(options),
                cancellationToken);

            var imagePaths = Directory
                .EnumerateFiles(workDirectory, "page-*.png")
                .OrderBy(GetGeneratedPageSortKey)
                .ToArray();

            var pages = new List<StructuredFinancialMetricsExtractedPage>(imagePaths.Length);

            for (var index = 0; index < imagePaths.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var imagePath = imagePaths[index];
                var textOutputPath = Path.Combine(
                    workDirectory,
                    $"{Path.GetFileNameWithoutExtension(imagePath)}-ocr");

                await RunProcessAsync(
                    options.TesseractPath,
                    [
                        imagePath,
                        textOutputPath,
                        "-l",
                        GetTesseractLanguage(options)
                    ],
                    GetTimeout(options),
                    cancellationToken);

                var textPath = textOutputPath + ".txt";
                var text = File.Exists(textPath)
                    ? await File.ReadAllTextAsync(textPath, cancellationToken)
                    : string.Empty;

                pages.Add(new StructuredFinancialMetricsExtractedPage(
                    PageNumber: GetGeneratedPageNumber(imagePath) ?? index + 1,
                    Text: text,
                    OcrConfidence: 0.6m));
            }

            return pages;
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
        }
    }

    private static async Task RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new PdfOcrDependencyException(DependencyMessage);
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
                throw new PdfOcrDependencyException(DependencyMessage);
            }
        }
        catch (Win32Exception)
        {
            throw new PdfOcrDependencyException(DependencyMessage);
        }
        catch (FileNotFoundException)
        {
            throw new PdfOcrDependencyException(DependencyMessage);
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
            throw new PdfOcrDependencyException(TimeoutMessage);
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            _ = stdout;

            throw new PdfOcrDependencyException(
                string.IsNullOrWhiteSpace(stderr)
                    ? DependencyMessage
                    : FailedMessage);
        }
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

    private static TimeSpan GetTimeout(StructuredFinancialMetricsPdfExtractionOptions options)
    {
        return TimeSpan.FromSeconds(Math.Max(1, options.OcrTimeoutSeconds));
    }

    private static string GetTesseractLanguage(StructuredFinancialMetricsPdfExtractionOptions options)
    {
        return string.IsNullOrWhiteSpace(options.TesseractLanguage)
            ? "eng"
            : options.TesseractLanguage.Trim();
    }
}
