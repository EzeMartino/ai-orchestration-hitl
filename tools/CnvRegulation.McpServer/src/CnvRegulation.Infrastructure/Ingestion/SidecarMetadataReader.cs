using System.Text.Json;
using CnvRegulation.Domain;

namespace CnvRegulation.Infrastructure.Ingestion;

/// <summary>
/// Reads and validates sidecar metadata for local regulation files.
/// </summary>
public sealed class SidecarMetadataReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Reads metadata and creates a document with the supplied parsed text.
    /// </summary>
    /// <param name="metadataPath">The sidecar metadata path.</param>
    /// <param name="text">The parsed source text.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The regulation document.</returns>
    public async Task<RegulationDocument> ReadDocumentAsync(
        string metadataPath,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataPath);

        await using var stream = File.OpenRead(metadataPath);
        var metadata = await JsonSerializer
            .DeserializeAsync<RegulationSourceMetadata>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (metadata is null)
        {
            throw new InvalidDataException($"Metadata file '{metadataPath}' is empty or invalid.");
        }

        Validate(metadata, metadataPath);

        return new RegulationDocument
        {
            Id = metadata.Id!.Trim(),
            Source = metadata.Source!.Trim(),
            DocumentType = metadata.DocumentType!.Trim(),
            ResolutionNumber = string.IsNullOrWhiteSpace(metadata.ResolutionNumber)
                ? null
                : metadata.ResolutionNumber.Trim(),
            Title = metadata.Title!.Trim(),
            PublicationDate = metadata.PublicationDate,
            EffectiveDate = metadata.EffectiveDate,
            Url = metadata.Url!.Trim(),
            Status = metadata.Status!.Trim(),
            RequiresReview = metadata.RequiresReview == true,
            Text = text
        };
    }

    private static void Validate(RegulationSourceMetadata metadata, string metadataPath)
    {
        var missingFields = new List<string>();

        AddIfMissing(metadata.Id, "id", missingFields);
        AddIfMissing(metadata.Source, "source", missingFields);
        AddIfMissing(metadata.DocumentType, "documentType", missingFields);
        AddIfMissing(metadata.Title, "title", missingFields);
        AddIfMissing(metadata.Url, "url", missingFields);
        AddIfMissing(metadata.Status, "status", missingFields);

        if (missingFields.Count > 0)
        {
            throw new InvalidDataException(
                $"Metadata file '{metadataPath}' is missing required fields: {string.Join(", ", missingFields)}.");
        }
    }

    private static void AddIfMissing(string? value, string fieldName, ICollection<string> missingFields)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missingFields.Add(fieldName);
        }
    }
}
