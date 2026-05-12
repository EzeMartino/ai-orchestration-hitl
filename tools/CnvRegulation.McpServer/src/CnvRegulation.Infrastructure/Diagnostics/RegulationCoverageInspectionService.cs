using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;

namespace CnvRegulation.Infrastructure.Diagnostics;

/// <summary>
/// Produces coverage diagnostics for ingested regulation repositories.
/// </summary>
public sealed class RegulationCoverageInspectionService(
    IRegulationRepository documentRepository,
    IRegulationChunkRepository chunkRepository,
    IChunkQualityInspectionService chunkQualityInspectionService) : IRegulationCoverageInspectionService
{
    /// <inheritdoc />
    public async Task<RegulationCoverageReport> InspectAsync(
        InspectCoverageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var documents = await documentRepository.ListAsync(cancellationToken).ConfigureAwait(false);
        var chunks = await chunkRepository.ListChunksAsync(cancellationToken).ConfigureAwait(false);
        var qualityReport = await chunkQualityInspectionService
            .InspectAsync(new InspectChunksRequest(), cancellationToken)
            .ConfigureAwait(false);
        var chunksByDocumentId = chunks
            .GroupBy(chunk => chunk.DocumentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var documentsWithZeroChunks = documents
            .Count(document => !chunksByDocumentId.ContainsKey(document.Id));
        var potentialWrapperDocuments = documents
            .Count(document => IsPotentialWrapperDocument(document, chunksByDocumentId));
        var searchableDocuments = documents.Count(IsSearchable);
        var nonSearchableDocuments = documents.Count - searchableDocuments;
        var searchableDocumentIds = documents
            .Where(IsSearchable)
            .Select(document => document.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var uniqueSearchableChunks = chunks
            .Where(chunk => searchableDocumentIds.Contains(chunk.DocumentId))
            .Where(chunk => string.IsNullOrWhiteSpace(chunk.DuplicateOfChunkId))
            .Select(chunk => string.IsNullOrWhiteSpace(chunk.ContentHash) ? chunk.Id : chunk.ContentHash)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var warnings = CreateWarnings(
            documentsWithZeroChunks,
            potentialWrapperDocuments,
            qualityReport.PossibleDuplicateChunks,
            qualityReport.VeryShortChunks,
            qualityReport.VeryLongChunks);

        return new RegulationCoverageReport
        {
            DocumentsTotal = documents.Count,
            SearchableDocuments = searchableDocuments,
            NonSearchableDocuments = nonSearchableDocuments,
            SourceDistribution = CreateDistribution(documents.Select(document => document.Source)),
            ResolutionNumberDistribution = CreateDistribution(documents.Select(document => document.ResolutionNumber)),
            ChunksTotal = chunks.Count,
            ChunksWithArticle = chunks.Count(chunk => !string.IsNullOrWhiteSpace(chunk.Article)),
            ChunksWithoutArticle = chunks.Count(chunk => string.IsNullOrWhiteSpace(chunk.Article)),
            DistinctArticleCount = chunks
                .Select(chunk => chunk.Article)
                .Where(article => !string.IsNullOrWhiteSpace(article))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            DuplicateUrlCount = CountDuplicateUrls(documents),
            DuplicateChunkCount = qualityReport.PossibleDuplicateChunks,
            DuplicateChunksHiddenByDefault = qualityReport.PossibleDuplicateChunks,
            UniqueSearchableChunks = uniqueSearchableChunks,
            VeryShortChunks = qualityReport.VeryShortChunks,
            VeryLongChunks = qualityReport.VeryLongChunks,
            DocumentsWithZeroChunks = documentsWithZeroChunks,
            PotentialWrapperDocuments = potentialWrapperDocuments,
            Warnings = warnings
        };
    }

    private static IReadOnlyList<CoverageDistributionItem> CreateDistribution(IEnumerable<string?> values) =>
        values
            .Select(value => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim())
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CoverageDistributionItem
            {
                Label = group.Key,
                Count = group.Count()
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static int CountDuplicateUrls(IReadOnlyList<RegulationDocument> documents) =>
        documents
            .Select(document => document.Url)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .GroupBy(url => url.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Sum(group => group.Count() - 1);

    private static bool IsPotentialWrapperDocument(
        RegulationDocument document,
        IReadOnlyDictionary<string, int> chunksByDocumentId)
    {
        var chunkCount = chunksByDocumentId.GetValueOrDefault(document.Id);
        var fileType = document.Metadata.TryGetValue("fileType", out var value) ? value : string.Empty;

        return string.Equals(document.Source, "Infoleg", StringComparison.OrdinalIgnoreCase)
            && string.Equals(fileType, "HTML", StringComparison.OrdinalIgnoreCase)
            && chunkCount <= 1
            && document.Text.Length >= 500;
    }

    private static bool IsSearchable(RegulationDocument document) =>
        !document.Metadata.TryGetValue("searchable", out var searchable)
        || !searchable.Equals("false", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> CreateWarnings(
        int documentsWithZeroChunks,
        int potentialWrapperDocuments,
        int duplicateChunks,
        int veryShortChunks,
        int veryLongChunks)
    {
        var warnings = new List<string>();

        if (documentsWithZeroChunks > 0)
        {
            warnings.Add($"{documentsWithZeroChunks} documents produced zero chunks");
        }

        if (potentialWrapperDocuments > 0)
        {
            warnings.Add($"{potentialWrapperDocuments} documents may be wrappers");
        }

        if (duplicateChunks > 0)
        {
            warnings.Add($"{duplicateChunks} possible duplicate chunks");
        }

        if (veryShortChunks > 0)
        {
            warnings.Add($"{veryShortChunks} very short chunks");
        }

        if (veryLongChunks > 0)
        {
            warnings.Add($"{veryLongChunks} very long chunks");
        }

        return warnings;
    }
}
