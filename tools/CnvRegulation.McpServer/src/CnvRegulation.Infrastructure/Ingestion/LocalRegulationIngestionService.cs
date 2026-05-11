using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;
using System.Text.Json;

namespace CnvRegulation.Infrastructure.Ingestion;

/// <summary>
/// Ingests local text and HTML regulation files with sidecar metadata.
/// </summary>
public sealed class LocalRegulationIngestionService(
    IRegulationRepository repository,
    IRegulationChunkRepository chunkRepository,
    IRegulationChunker chunker,
    PlainTextRegulationParser plainTextParser,
    HtmlRegulationParser htmlParser,
    IPdfTextExtractor pdfTextExtractor,
    SidecarMetadataReader metadataReader) : IRegulationIngestionService
{
    private static readonly string[] SupportedExtensions = [".txt", ".html", ".htm", ".pdf"];

    /// <inheritdoc />
    public async Task<IngestRegulationSourceResponse> IngestAsync(
        IngestRegulationSourceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.SourceDirectory))
        {
            return new IngestRegulationSourceResponse
            {
                DocumentsIngested = 0,
                DocumentsSkipped = 0,
                Warnings = ["Source directory is required."]
            };
        }

        if (!Directory.Exists(request.SourceDirectory))
        {
            return new IngestRegulationSourceResponse
            {
                DocumentsIngested = 0,
                DocumentsSkipped = 0,
                Warnings = [$"Source directory does not exist: {request.SourceDirectory}"]
            };
        }

        var warnings = new List<string>();
        var documentsIngested = 0;
        var documentsSkipped = 0;

        foreach (var sourceFile in EnumerateSourceFiles(request.SourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extension = Path.GetExtension(sourceFile);
            if (!SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                documentsSkipped++;
                warnings.Add($"Skipped unsupported file type: {extension} for '{Path.GetFileName(sourceFile)}'.");
                continue;
            }

            var metadataFile = GetMetadataFilePath(sourceFile);
            if (!File.Exists(metadataFile))
            {
                documentsSkipped++;
                warnings.Add($"Skipping '{Path.GetFileName(sourceFile)}' because metadata file is missing.");
                continue;
            }

            try
            {
                var parsed = await ParseSourceFileAsync(sourceFile, cancellationToken).ConfigureAwait(false);
                var document = await metadataReader
                    .ReadDocumentAsync(metadataFile, parsed.Text, cancellationToken)
                    .ConfigureAwait(false);
                document = AddParsedMetadata(document, parsed.Metadata);

                await repository.SaveAsync(document, cancellationToken).ConfigureAwait(false);
                var chunks = await chunker.ChunkAsync(document, cancellationToken).ConfigureAwait(false);
                await chunkRepository.ReplaceForDocumentAsync(document.Id, chunks, cancellationToken).ConfigureAwait(false);
                warnings.AddRange(parsed.Warnings.Select(warning => $"'{Path.GetFileName(sourceFile)}': {warning}"));
                documentsIngested++;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException)
            {
                documentsSkipped++;
                warnings.Add($"Skipping '{Path.GetFileName(sourceFile)}': {exception.Message}");
            }
        }

        return new IngestRegulationSourceResponse
        {
            DocumentsIngested = documentsIngested,
            DocumentsSkipped = documentsSkipped,
            Warnings = warnings
        };
    }

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
            var metadata = CreateSourceMetadata("PDF", fileName);
            metadata["extractionMethod"] = "PdfPig";
            metadata["pageCount"] = extraction.Pages.Count.ToString();

            return new ParsedRegulationSource
            {
                Text = extraction.Text,
                Metadata = metadata,
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

    private static RegulationDocument AddParsedMetadata(
        RegulationDocument document,
        IReadOnlyDictionary<string, string> parsedMetadata)
    {
        var metadata = new Dictionary<string, string>(document.Metadata, StringComparer.OrdinalIgnoreCase);
        foreach (var item in parsedMetadata)
        {
            metadata[item.Key] = item.Value;
        }

        return new RegulationDocument
        {
            Id = document.Id,
            Source = document.Source,
            DocumentType = document.DocumentType,
            ResolutionNumber = document.ResolutionNumber,
            Title = document.Title,
            PublicationDate = document.PublicationDate,
            EffectiveDate = document.EffectiveDate,
            Url = document.Url,
            Status = document.Status,
            RequiresReview = document.RequiresReview,
            RetrievedAt = document.RetrievedAt,
            Metadata = metadata,
            Text = document.Text
        };
    }
}
