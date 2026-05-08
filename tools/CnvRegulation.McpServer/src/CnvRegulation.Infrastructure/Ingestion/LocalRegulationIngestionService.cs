using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
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
    SidecarMetadataReader metadataReader) : IRegulationIngestionService
{
    private static readonly string[] SupportedExtensions = [".txt", ".html", ".htm"];

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
                var text = await ParseSourceFileAsync(sourceFile, cancellationToken).ConfigureAwait(false);
                var document = await metadataReader
                    .ReadDocumentAsync(metadataFile, text, cancellationToken)
                    .ConfigureAwait(false);

                await repository.SaveAsync(document, cancellationToken).ConfigureAwait(false);
                var chunks = await chunker.ChunkAsync(document, cancellationToken).ConfigureAwait(false);
                await chunkRepository.ReplaceForDocumentAsync(document.Id, chunks, cancellationToken).ConfigureAwait(false);
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
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase);

    private static string GetMetadataFilePath(string sourceFile)
    {
        var directory = Path.GetDirectoryName(sourceFile) ?? string.Empty;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sourceFile);

        return Path.Combine(directory, $"{fileNameWithoutExtension}.metadata.json");
    }

    private Task<string> ParseSourceFileAsync(string sourceFile, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(sourceFile);

        return extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            ? plainTextParser.ParseAsync(sourceFile, cancellationToken)
            : htmlParser.ParseAsync(sourceFile, cancellationToken);
    }
}
