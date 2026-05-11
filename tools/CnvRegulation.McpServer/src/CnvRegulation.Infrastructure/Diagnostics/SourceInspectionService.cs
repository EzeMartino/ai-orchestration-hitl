using System.Text.Json;
using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;
using CnvRegulation.Infrastructure.Ingestion;

namespace CnvRegulation.Infrastructure.Diagnostics;

/// <summary>
/// Inspects local source files and reports parsing/chunking diagnostics.
/// </summary>
public sealed class SourceInspectionService(
    PlainTextRegulationParser plainTextParser,
    HtmlRegulationParser htmlParser,
    IPdfTextExtractor pdfTextExtractor,
    ITextNormalizer pdfTextNormalizer,
    SidecarMetadataReader metadataReader,
    IRegulationChunker chunker) : ISourceInspectionService
{
    private static readonly string[] SupportedExtensions = [".txt", ".html", ".htm", ".pdf"];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc />
    public async Task<InspectSourcesResponse> InspectAsync(
        InspectSourcesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.SourceDirectory))
        {
            return CreateEmptyResponse("Source directory is required.");
        }

        if (!Directory.Exists(request.SourceDirectory))
        {
            return CreateEmptyResponse($"Source directory does not exist: {request.SourceDirectory}");
        }

        var documents = new List<SourceInspectionDocumentResult>();
        var responseWarnings = new List<string>();

        foreach (var sourceFile in EnumerateSourceFiles(request.SourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            documents.Add(await InspectFileAsync(sourceFile, cancellationToken).ConfigureAwait(false));
        }

        var supportedDocuments = documents.Where(document => !document.IsUnsupported).ToArray();

        return new InspectSourcesResponse
        {
            DocumentsInspected = documents.Count,
            DocumentsWithChunks = supportedDocuments.Count(document => document.ChunkCount > 0),
            DocumentsWithoutChunks = supportedDocuments.Count(document => document.ChunkCount == 0),
            UnsupportedFiles = documents.Count(document => document.IsUnsupported),
            Documents = documents,
            Warnings = responseWarnings
        };
    }

    private async Task<SourceInspectionDocumentResult> InspectFileAsync(
        string sourceFile,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var extension = Path.GetExtension(sourceFile);
        var metadataPath = GetMetadataFilePath(sourceFile);
        var metadata = await TryReadMetadataAsync(metadataPath, warnings, cancellationToken).ConfigureAwait(false);

        if (!SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            warnings.Add($"unsupported file type: {extension}");
            AddReviewWarnings(metadata, warnings);

            return new SourceInspectionDocumentResult
            {
                FileName = Path.GetFileName(sourceFile),
                Source = metadata?.Source,
                Title = metadata?.Title,
                DocumentType = metadata?.DocumentType,
                FileType = ToFileType(extension),
                ExtractedTextLength = 0,
                ChunkCount = 0,
                DetectedTitles = [],
                DetectedChapters = [],
                DetectedSections = [],
                DetectedArticles = [],
                FirstArticles = [],
                IsUnsupported = true,
                Warnings = warnings
            };
        }

        if (!File.Exists(metadataPath))
        {
            warnings.Add("metadata file is missing");

            return new SourceInspectionDocumentResult
            {
                FileName = Path.GetFileName(sourceFile),
                Source = null,
                Title = null,
                DocumentType = null,
                FileType = ToFileType(extension),
                ExtractedTextLength = 0,
                ChunkCount = 0,
                DetectedTitles = [],
                DetectedChapters = [],
                DetectedSections = [],
                DetectedArticles = [],
                FirstArticles = [],
                IsUnsupported = false,
                Warnings = warnings
            };
        }

        try
        {
            var parsed = await ParseSourceFileAsync(sourceFile, cancellationToken).ConfigureAwait(false);
            var document = await metadataReader.ReadDocumentAsync(metadataPath, parsed.Text, cancellationToken).ConfigureAwait(false);
            var chunks = await chunker.ChunkAsync(document, cancellationToken).ConfigureAwait(false);
            warnings.AddRange(parsed.Warnings);
            AddReviewWarnings(metadata, warnings);

            if (chunks.Count == 0)
            {
                warnings.Add("no article boundaries detected");
            }

            return CreateDocumentResult(sourceFile, document, chunks, parsed, warnings);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException)
        {
            warnings.Add(exception.Message);

            return new SourceInspectionDocumentResult
            {
                FileName = Path.GetFileName(sourceFile),
                Source = metadata?.Source,
                Title = metadata?.Title,
                DocumentType = metadata?.DocumentType,
                FileType = ToFileType(extension),
                ExtractedTextLength = 0,
                ChunkCount = 0,
                DetectedTitles = [],
                DetectedChapters = [],
                DetectedSections = [],
                DetectedArticles = [],
                FirstArticles = [],
                IsUnsupported = false,
                Warnings = warnings
            };
        }
    }

    private async Task<RegulationSourceMetadata?> TryReadMetadataAsync(
        string metadataPath,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(metadataPath);
            return await JsonSerializer
                .DeserializeAsync<RegulationSourceMetadata>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            warnings.Add($"metadata file is invalid: {exception.Message}");
            return null;
        }
    }

    private static SourceInspectionDocumentResult CreateDocumentResult(
        string sourceFile,
        RegulationDocument document,
        IReadOnlyList<RegulationChunk> chunks,
        ParsedRegulationSource parsed,
        IReadOnlyList<string> warnings)
    {
        var detectedTitles = chunks
            .Select(chunk => chunk.Title)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();
        var detectedChapters = chunks
            .Select(chunk => chunk.Chapter)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();
        var detectedSections = chunks
            .Select(chunk => chunk.Section)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();
        var detectedArticles = chunks
            .Select(chunk => chunk.Article)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();

        return new SourceInspectionDocumentResult
        {
            FileName = Path.GetFileName(sourceFile),
            Source = document.Source,
            Title = document.Title,
            DocumentType = document.DocumentType,
            FileType = parsed.FileType,
            PageCount = parsed.PageCount,
            ExtractedTextLength = document.Text.Length,
            ChunkCount = chunks.Count,
            DetectedTitles = detectedTitles,
            DetectedChapters = detectedChapters,
            DetectedSections = detectedSections,
            DetectedArticles = detectedArticles,
            FirstArticles = detectedArticles.Take(3).ToArray(),
            IsUnsupported = false,
            Warnings = warnings
        };
    }

    private static void AddReviewWarnings(RegulationSourceMetadata? metadata, ICollection<string> warnings)
    {
        if (string.Equals(metadata?.Status, "candidate", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("candidate source");
        }

        if (metadata?.RequiresReview == true)
        {
            warnings.Add("requires review");
        }
    }

    private static InspectSourcesResponse CreateEmptyResponse(string warning) =>
        new()
        {
            DocumentsInspected = 0,
            DocumentsWithChunks = 0,
            DocumentsWithoutChunks = 0,
            UnsupportedFiles = 0,
            Documents = [],
            Warnings = [warning]
        };

    private static IEnumerable<string> EnumerateSourceFiles(string sourceDirectory) =>
        Directory
            .EnumerateFiles(sourceDirectory)
            .Where(file => !file.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase))
            .Where(file => !Path.GetFileName(file).StartsWith(".", StringComparison.Ordinal))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase);

    private static string GetMetadataFilePath(string sourceFile)
    {
        var directory = Path.GetDirectoryName(sourceFile) ?? string.Empty;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sourceFile);

        return Path.Combine(directory, $"{fileNameWithoutExtension}.metadata.json");
    }

    private async Task<ParsedRegulationSource> ParseSourceFileAsync(
        string sourceFile,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(sourceFile);
        var fileName = Path.GetFileName(sourceFile);

        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return CreateParsedSource(
                await plainTextParser.ParseAsync(sourceFile, cancellationToken).ConfigureAwait(false),
                "TXT",
                fileName);
        }

        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var extraction = await pdfTextExtractor.ExtractAsync(sourceFile, cancellationToken).ConfigureAwait(false);

            return new ParsedRegulationSource
            {
                Text = pdfTextNormalizer.Normalize(extraction.Text),
                Metadata = CreateSourceMetadata("PDF", fileName),
                Warnings = extraction.Warnings,
                FileType = "PDF",
                PageCount = extraction.Pages.Count
            };
        }

        return CreateParsedSource(
            await htmlParser.ParseAsync(sourceFile, cancellationToken).ConfigureAwait(false),
            "HTML",
            fileName);
    }

    private static ParsedRegulationSource CreateParsedSource(string text, string fileType, string fileName) =>
        new()
        {
            Text = text,
            Metadata = CreateSourceMetadata(fileType, fileName),
            Warnings = [],
            FileType = fileType
        };

    private static Dictionary<string, string> CreateSourceMetadata(string fileType, string fileName) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["sourceFile"] = fileName,
            ["fileType"] = fileType
        };

    private static string ToFileType(string extension) =>
        extension.TrimStart('.').ToUpperInvariant();
}
