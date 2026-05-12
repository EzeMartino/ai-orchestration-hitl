using System.Text.Json;
using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;

namespace CnvRegulation.Infrastructure.Sources;

/// <summary>
/// Creates a curated initial manifest of CNV and Infoleg source candidates.
/// </summary>
public sealed class CuratedSourceDiscoveryService(TimeProvider timeProvider) : ISourceDiscoveryService
{
    /// <inheritdoc />
    public async Task<DiscoverSourcesResponse> DiscoverAsync(
        DiscoverSourcesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ManifestPath))
        {
            return new DiscoverSourcesResponse
            {
                ManifestPath = string.Empty,
                SourcesDiscovered = 0,
                Warnings = ["Manifest path is required."]
            };
        }

        var manifest = CreateManifest(timeProvider.GetUtcNow());
        var directory = Path.GetDirectoryName(request.ManifestPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(request.ManifestPath);
        await JsonSerializer
            .SerializeAsync(stream, manifest, SourceManifestJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return new DiscoverSourcesResponse
        {
            ManifestPath = request.ManifestPath,
            SourcesDiscovered = manifest.Sources.Count,
            Warnings = ["All discovered sources are candidates and require regulatory review before use."]
        };
    }

    private static SourceManifest CreateManifest(DateTimeOffset generatedAt) =>
        new()
        {
            GeneratedAt = generatedAt,
            Source = "CuratedSourceDiscovery",
            Sources =
            [
                new SourceManifestItem
                {
                    Id = "cnv-toc-2013",
                    Source = "CNV",
                    DocumentType = "Texto Ordenado",
                    Title = "Normas CNV N.T. 2013",
                    Url = "https://www.cnv.gov.ar/sitioWeb/Content/assets/files/TOC2013.pdf",
                    FileName = "cnv-toc-2013.pdf",
                    MetadataFileName = "cnv-toc-2013.metadata.json",
                    Status = "candidate",
                    Priority = "high",
                    RequiresReview = true,
                    PublicationDate = new DateOnly(2013, 9, 9)
                },
                new SourceManifestItem
                {
                    Id = "infoleg-rg-622-2013-norma",
                    Source = "Infoleg",
                    DocumentType = "Resolución General",
                    ResolutionNumber = "622/2013",
                    Title = "Resolución General 622/2013 - Texto completo",
                    Url = "https://servicios.infoleg.gob.ar/infolegInternet/anexos/215000-219999/219405/norma.htm",
                    FileName = "infoleg-rg-622-2013-norma.html",
                    MetadataFileName = "infoleg-rg-622-2013-norma.metadata.json",
                    Status = "candidate",
                    Priority = "high",
                    RequiresReview = true,
                    PublicationDate = new DateOnly(2013, 9, 9)
                },
                new SourceManifestItem
                {
                    Id = "infoleg-rg-622-2013-texact",
                    Source = "Infoleg",
                    DocumentType = "Resolución General",
                    ResolutionNumber = "622/2013",
                    Title = "Resolución General 622/2013 - Texto actualizado",
                    Url = "https://servicios.infoleg.gob.ar/infolegInternet/anexos/215000-219999/219405/texact.htm",
                    FileName = "infoleg-rg-622-2013-texact.html",
                    MetadataFileName = "infoleg-rg-622-2013-texact.metadata.json",
                    Status = "candidate",
                    Priority = "high",
                    RequiresReview = true,
                    PublicationDate = new DateOnly(2013, 9, 9)
                },
                new SourceManifestItem
                {
                    Id = "cnv-marco-regulatorio",
                    Source = "CNV",
                    DocumentType = "Marco Regulatorio",
                    Title = "CNV Marco Regulatorio",
                    Url = "https://www.cnv.gov.ar/sitioWeb/MarcoRegulatorio?panel=3",
                    FileName = "cnv-marco-regulatorio.html",
                    MetadataFileName = "cnv-marco-regulatorio.metadata.json",
                    Status = "candidate",
                    Priority = "medium",
                    RequiresReview = true
                }
            ]
        };
}
