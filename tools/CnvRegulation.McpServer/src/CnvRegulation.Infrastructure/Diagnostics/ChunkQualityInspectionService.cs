using System.Text.RegularExpressions;
using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;

namespace CnvRegulation.Infrastructure.Diagnostics;

/// <summary>
/// Produces quality diagnostics for stored regulation chunks.
/// </summary>
public sealed partial class ChunkQualityInspectionService(
    IRegulationRepository documentRepository,
    IRegulationChunkRepository chunkRepository) : IChunkQualityInspectionService
{
    private const int VeryShortThreshold = 80;
    private const int VeryLongThreshold = 10_000;
    private const int HeaderFooterLineThreshold = 5;

    /// <inheritdoc />
    public async Task<ChunkQualityReport> InspectAsync(
        InspectChunksRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var documents = await documentRepository.ListAsync(cancellationToken).ConfigureAwait(false);
        var chunks = await chunkRepository.ListChunksAsync(cancellationToken).ConfigureAwait(false);
        var repeatedLines = FindRepeatedHeaderFooterLines(chunks);
        var duplicateChunkIds = FindDuplicateChunkIds(chunks);
        var warnings = new List<ChunkQualityWarning>();

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddChunkWarnings(chunk, duplicateChunkIds, repeatedLines, warnings);
        }

        var lengths = chunks.Select(chunk => chunk.Text?.Length ?? 0).Order().ToArray();
        var pollutedChunkIds = warnings
            .Where(warning => warning.WarningType == "repeated_header_footer")
            .Select(warning => warning.ChunkId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suspiciousChunkIds = warnings
            .Select(warning => warning.ChunkId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new ChunkQualityReport
        {
            DocumentsAnalyzed = documents.Count,
            ChunksAnalyzed = chunks.Count,
            EmptyChunks = warnings.Count(warning => warning.WarningType == "empty"),
            VeryShortChunks = warnings.Count(warning => warning.WarningType == "very_short"),
            VeryLongChunks = warnings.Count(warning => warning.WarningType == "very_long"),
            ChunksWithoutArticle = warnings.Count(warning => warning.WarningType == "missing_article"),
            PossibleDuplicateChunks = warnings.Count(warning => warning.WarningType == "possible_duplicate"),
            SuspiciousHeaderFooterPollution = pollutedChunkIds.Count,
            SuspiciousChunks = suspiciousChunkIds.Count,
            AverageChunkLength = lengths.Length == 0 ? 0 : lengths.Average(),
            MedianChunkLength = CalculateMedian(lengths),
            Warnings = warnings
        };
    }

    private static void AddChunkWarnings(
        RegulationChunk chunk,
        IReadOnlySet<string> duplicateChunkIds,
        IReadOnlySet<string> repeatedLines,
        ICollection<ChunkQualityWarning> warnings)
    {
        var length = chunk.Text?.Length ?? 0;

        if (length == 0)
        {
            warnings.Add(CreateWarning(chunk, "empty", "empty chunk"));
            return;
        }

        if (length < VeryShortThreshold)
        {
            warnings.Add(CreateWarning(chunk, "very_short", $"very short chunk, {length} chars"));
        }

        if (length > VeryLongThreshold)
        {
            warnings.Add(CreateWarning(chunk, "very_long", $"very long chunk, {length} chars"));
        }

        if (string.IsNullOrWhiteSpace(chunk.Article))
        {
            warnings.Add(CreateWarning(chunk, "missing_article", "no article detected"));
        }

        if (duplicateChunkIds.Contains(chunk.Id))
        {
            warnings.Add(CreateWarning(chunk, "possible_duplicate", "possible duplicate chunk text"));
        }

        if (ContainsRepeatedHeaderFooterText(chunk.Text ?? string.Empty, repeatedLines))
        {
            warnings.Add(CreateWarning(chunk, "repeated_header_footer", "possible repeated header/footer text"));
        }
    }

    private static IReadOnlySet<string> FindDuplicateChunkIds(IReadOnlyList<RegulationChunk> chunks) =>
        chunks
            .Select(chunk => new
            {
                Chunk = chunk,
                TextKey = NormalizeChunkText(chunk.Text)
            })
            .Where(item => item.TextKey.Length > 0)
            .GroupBy(item => item.TextKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(item => item.Chunk.Id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlySet<string> FindRepeatedHeaderFooterLines(IReadOnlyList<RegulationChunk> chunks) =>
        chunks
            .SelectMany(chunk => ReadLines(chunk.Text))
            .Select(NormalizeLineKey)
            .Where(line => line.Length is >= 8 and <= 120)
            .Where(line => !LegalMarkerLineRegex().IsMatch(line))
            .GroupBy(line => line, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= HeaderFooterLineThreshold)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool ContainsRepeatedHeaderFooterText(string text, IReadOnlySet<string> repeatedLines) =>
        repeatedLines.Count > 0
        && ReadLines(text).Select(NormalizeLineKey).Any(repeatedLines.Contains);

    private static IEnumerable<string> ReadLines(string text) =>
        text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    private static string NormalizeChunkText(string text) =>
        NonWordRegex()
            .Replace(text.ToUpperInvariant(), " ")
            .Trim();

    private static string NormalizeLineKey(string line) =>
        ExcessiveWhitespaceRegex()
            .Replace(line.Trim(), " ")
            .ToUpperInvariant();

    private static double CalculateMedian(IReadOnlyList<int> lengths)
    {
        if (lengths.Count == 0)
        {
            return 0;
        }

        var middle = lengths.Count / 2;
        return lengths.Count % 2 == 1
            ? lengths[middle]
            : (lengths[middle - 1] + lengths[middle]) / 2.0;
    }

    private static ChunkQualityWarning CreateWarning(RegulationChunk chunk, string type, string message) =>
        new()
        {
            DocumentId = chunk.DocumentId,
            ChunkId = chunk.Id,
            Article = chunk.Article,
            WarningType = type,
            Message = message
        };

    [GeneratedRegex(@"\W+", RegexOptions.Compiled)]
    private static partial Regex NonWordRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex ExcessiveWhitespaceRegex();

    [GeneratedRegex(@"^(ARTÍCULO|ARTICULO|TÍTULO|TITULO|CAPÍTULO|CAPITULO|SECCIÓN|SECCION)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LegalMarkerLineRegex();
}
