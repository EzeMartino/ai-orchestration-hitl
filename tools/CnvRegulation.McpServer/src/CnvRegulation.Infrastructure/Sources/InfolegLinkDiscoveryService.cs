using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Infrastructure.Ingestion;

namespace CnvRegulation.Infrastructure.Sources;

/// <summary>
/// Discovers official related Infoleg links from already-downloaded Infoleg HTML files.
/// </summary>
public sealed partial class InfolegLinkDiscoveryService(
    InfolegLinkExtractor extractor,
    TimeProvider timeProvider) : IInfolegLinkDiscoveryService
{
    /// <inheritdoc />
    public async Task<InfolegLinkDiscoveryResponse> DiscoverAsync(
        InfolegLinkDiscoveryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.SourceDirectory))
        {
            return CreateEmptyResponse(request.OutputManifestPath, "Source directory is required.");
        }

        if (!Directory.Exists(request.SourceDirectory))
        {
            return CreateEmptyResponse(
                request.OutputManifestPath,
                $"Source directory does not exist: {request.SourceDirectory}");
        }

        if (string.IsNullOrWhiteSpace(request.OutputManifestPath))
        {
            return CreateEmptyResponse(string.Empty, "Output manifest path is required.");
        }

        var warnings = new List<string>();
        var acceptedByUrl = new Dictionary<string, SourceManifestItem>(StringComparer.OrdinalIgnoreCase);
        var linksDiscovered = 0;
        var linksSkipped = 0;
        var filesInspected = 0;
        var maxLinks = request.MaxLinksPerSource <= 0 ? 20 : Math.Min(request.MaxLinksPerSource, 100);

        foreach (var sourceFile in EnumerateHtmlFiles(request.SourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = await TryReadMetadataAsync(sourceFile, cancellationToken).ConfigureAwait(false);
            if (!IsInfolegSource(metadata))
            {
                continue;
            }

            filesInspected++;
            var html = await File.ReadAllTextAsync(sourceFile, cancellationToken).ConfigureAwait(false);
            var baseUri = new Uri(metadata!.Url!);
            var extraction = extractor.Extract(html, baseUri);
            linksDiscovered += extraction.LinksDiscovered;
            linksSkipped += extraction.LinksSkipped;

            var acceptedForSource = 0;
            foreach (var candidate in extraction.Candidates)
            {
                if (acceptedForSource >= maxLinks)
                {
                    linksSkipped++;
                    continue;
                }

                if (acceptedByUrl.ContainsKey(candidate.Url))
                {
                    linksSkipped++;
                    continue;
                }

                acceptedByUrl.Add(candidate.Url, CreateManifestItem(candidate, metadata!));
                acceptedForSource++;
            }
        }

        var manifest = new SourceManifest
        {
            GeneratedAt = timeProvider.GetUtcNow(),
            Source = "InfolegLinkDiscovery",
            Sources = acceptedByUrl.Values
                .OrderBy(source => source.Url, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        var directory = Path.GetDirectoryName(request.OutputManifestPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(request.OutputManifestPath);
        await JsonSerializer
            .SerializeAsync(stream, manifest, SourceManifestJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return new InfolegLinkDiscoveryResponse
        {
            InfolegFilesInspected = filesInspected,
            LinksDiscovered = linksDiscovered,
            LinksAccepted = manifest.Sources.Count,
            LinksSkipped = linksSkipped,
            ManifestPath = request.OutputManifestPath,
            Warnings = warnings
        };
    }

    private static IEnumerable<string> EnumerateHtmlFiles(string sourceDirectory) =>
        Directory
            .EnumerateFiles(sourceDirectory)
            .Where(file =>
                Path.GetExtension(file).Equals(".html", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(file).Equals(".htm", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase);

    private static async Task<RegulationSourceMetadata?> TryReadMetadataAsync(
        string sourceFile,
        CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(
            Path.GetDirectoryName(sourceFile) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(sourceFile)}.metadata.json");

        if (!File.Exists(metadataPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(metadataPath);
        return await JsonSerializer
            .DeserializeAsync<RegulationSourceMetadata>(stream, SourceManifestJson.Options, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsInfolegSource(RegulationSourceMetadata? metadata)
    {
        if (metadata is null
            || !string.Equals(metadata.Source, "Infoleg", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(metadata.Url))
        {
            return false;
        }

        return Uri.TryCreate(metadata.Url, UriKind.Absolute, out var uri)
            && string.Equals(uri.Host, "servicios.infoleg.gob.ar", StringComparison.OrdinalIgnoreCase);
    }

    private static SourceManifestItem CreateManifestItem(
        InfolegLinkCandidate candidate,
        RegulationSourceMetadata metadata)
    {
        var classification = candidate.Classification.ToString();
        var documentId = ExtractInfolegDocumentId(candidate.Url);
        var idSuffix = documentId ?? CreateShortHash(candidate.Url);
        var kind = classification.ToLowerInvariant();
        var id = $"infoleg-discovered-{idSuffix}-{kind}";
        var extension = Path.GetExtension(new Uri(candidate.Url).AbsolutePath);
        extension = string.IsNullOrWhiteSpace(extension) || extension.Equals(".do", StringComparison.OrdinalIgnoreCase)
            ? ".html"
            : extension;
        var priority = candidate.Classification == InfolegLinkClassification.Unknown ? "low" : "medium";

        return new SourceManifestItem
        {
            Id = id,
            Source = "Infoleg",
            DocumentType = metadata.DocumentType ?? "Infoleg",
            ResolutionNumber = metadata.ResolutionNumber,
            Title = "Discovered Infoleg source",
            Url = candidate.Url,
            FileName = SourceFileNameSanitizer.Sanitize($"{id}{extension}", $"{id}.html"),
            MetadataFileName = SourceFileNameSanitizer.Sanitize($"{id}.metadata.json", $"{id}.metadata.json"),
            Status = "candidate",
            Priority = priority,
            RequiresReview = true,
            PublicationDate = metadata.PublicationDate,
            EffectiveDate = metadata.EffectiveDate
        };
    }

    private static string? ExtractInfolegDocumentId(string url)
    {
        var match = InfolegDocumentIdRegex().Match(url);
        if (match.Success)
        {
            return match.Groups["id"].Value;
        }

        var verNormaMatch = VerNormaIdRegex().Match(url);
        return verNormaMatch.Success ? verNormaMatch.Groups["id"].Value : null;
    }

    private static string CreateShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..10].ToLowerInvariant();
    }

    private static InfolegLinkDiscoveryResponse CreateEmptyResponse(string manifestPath, string warning) =>
        new()
        {
            InfolegFilesInspected = 0,
            LinksDiscovered = 0,
            LinksAccepted = 0,
            LinksSkipped = 0,
            ManifestPath = manifestPath,
            Warnings = [warning]
        };

    [GeneratedRegex(@"/(?<id>\d+)/(?:norma|texact)\.htm", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex InfolegDocumentIdRegex();

    [GeneratedRegex(@"[?&](?:id|nro)=(?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex VerNormaIdRegex();
}
