using System.Net;
using System.Text.Json;
using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Infrastructure.Sources;

/// <summary>
/// Downloads source candidates listed in a source manifest.
/// </summary>
public sealed class ManifestSourceDownloadService(HttpClient httpClient, TimeProvider timeProvider) : ISourceDownloadService
{
    private static readonly string[] AllowedExtensions = [".html", ".htm", ".pdf", ".txt"];

    /// <inheritdoc />
    public async Task<DownloadSourcesResponse> DownloadAsync(
        DownloadSourcesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.ManifestPath))
        {
            return new DownloadSourcesResponse
            {
                SourcesDownloaded = 0,
                SourcesSkipped = 0,
                Warnings = [$"Manifest file does not exist: {request.ManifestPath}"]
            };
        }

        Directory.CreateDirectory(request.OutputDirectory);

        await using var stream = File.OpenRead(request.ManifestPath);
        var manifest = await JsonSerializer
            .DeserializeAsync<SourceManifest>(stream, SourceManifestJson.Options, cancellationToken)
            .ConfigureAwait(false);

        if (manifest is null)
        {
            return new DownloadSourcesResponse
            {
                SourcesDownloaded = 0,
                SourcesSkipped = 0,
                Warnings = [$"Manifest file is empty or invalid: {request.ManifestPath}"]
            };
        }

        var warnings = new List<string>();
        var downloaded = 0;
        var skipped = 0;

        foreach (var source in manifest.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
            {
                skipped++;
                warnings.Add($"Skipping '{source.Id}' because URL is invalid.");
                continue;
            }

            try
            {
                var safeFileName = SourceFileNameSanitizer.Sanitize(source.FileName, CreateFallbackFileName(source, uri));
                var safeMetadataFileName = SourceFileNameSanitizer.Sanitize(
                    source.MetadataFileName,
                    $"{Path.GetFileNameWithoutExtension(safeFileName)}.metadata.json");
                if (!IsAllowedSourceExtension(safeFileName, uri))
                {
                    skipped++;
                    warnings.Add($"Skipping '{source.Id}' because file type is not supported for download.");
                    continue;
                }

                var outputPath = Path.Combine(request.OutputDirectory, safeFileName);
                var metadataPath = Path.Combine(request.OutputDirectory, safeMetadataFileName);

                using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
                httpRequest.Headers.UserAgent.ParseAdd("Mozilla/5.0 CnvRegulationMcpServer/0.1");
                httpRequest.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/pdf,*/*");

                using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.NotFound)
                {
                    skipped++;
                    warnings.Add($"Skipping '{source.Id}' because the source returned 404.");
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                await File.WriteAllBytesAsync(outputPath, bytes, cancellationToken).ConfigureAwait(false);
                await WriteMetadataAsync(metadataPath, source, cancellationToken).ConfigureAwait(false);

                downloaded++;

                if (Path.GetExtension(safeFileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"Downloaded '{safeFileName}' as candidate; PDF text extraction still requires regulatory review.");
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException)
            {
                skipped++;
                warnings.Add($"Skipping '{source.Id}': {exception.Message}");
            }
        }

        return new DownloadSourcesResponse
        {
            SourcesDownloaded = downloaded,
            SourcesSkipped = skipped,
            Warnings = warnings
        };
    }

    private async Task WriteMetadataAsync(
        string metadataPath,
        SourceManifestItem source,
        CancellationToken cancellationToken)
    {
        var metadata = new DownloadedSourceMetadata
        {
            Id = source.Id,
            Source = source.Source,
            DocumentType = source.DocumentType,
            ResolutionNumber = source.ResolutionNumber,
            Title = source.Title,
            PublicationDate = source.PublicationDate,
            EffectiveDate = source.EffectiveDate,
            Url = source.Url,
            Status = "candidate",
            RequiresReview = true,
            RetrievedAt = timeProvider.GetUtcNow()
        };

        await using var stream = File.Create(metadataPath);
        await JsonSerializer
            .SerializeAsync(stream, metadata, SourceManifestJson.Options, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string CreateFallbackFileName(SourceManifestItem source, Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath);
        extension = string.IsNullOrWhiteSpace(extension) ? ".html" : extension;

        return $"{source.Id}{extension}";
    }

    private static bool IsAllowedSourceExtension(string fileName, Uri uri)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = Path.GetExtension(uri.AbsolutePath);
        }

        return AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}
