using CnvRegulation.Application.Abstractions;
using CnvRegulation.Domain;

namespace CnvRegulation.Infrastructure.Chunking;

/// <summary>
/// Splits legal regulation documents into article-level chunks while preserving detected structure.
/// </summary>
public class LegalStructureRegulationChunker(LegalStructureDetector structureDetector) : IRegulationChunker
{
    /// <inheritdoc />
    public Task<IReadOnlyList<RegulationChunk>> ChunkAsync(
        RegulationDocument document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        var chunks = new List<RegulationChunk>();
        var currentLines = new List<string>();
        var currentLegalTitle = default(string);
        var currentChapter = default(string);
        var currentSection = default(string);
        var currentArticle = default(string);
        var articleOccurrenceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in ReadLines(document.Text))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var detectedTitle = structureDetector.DetectTitle(line);
            if (detectedTitle is not null)
            {
                AddCurrentChunk();
                currentLegalTitle = detectedTitle;
                currentChapter = null;
                currentSection = null;
                continue;
            }

            var detectedChapter = structureDetector.DetectChapter(line);
            if (detectedChapter is not null)
            {
                AddCurrentChunk();
                currentChapter = detectedChapter;
                currentSection = null;
                continue;
            }

            var detectedSection = structureDetector.DetectSection(line);
            if (detectedSection is not null)
            {
                AddCurrentChunk();
                currentSection = detectedSection;
                continue;
            }

            var detectedArticle = structureDetector.DetectArticle(line);
            if (detectedArticle is not null)
            {
                AddCurrentChunk();
                currentArticle = detectedArticle;
                currentLines.Add(line);
                continue;
            }

            if (currentArticle is not null)
            {
                currentLines.Add(line);
            }
        }

        AddCurrentChunk();

        return Task.FromResult<IReadOnlyList<RegulationChunk>>(chunks);

        void AddCurrentChunk()
        {
            if (currentArticle is null || currentLines.Count == 0)
            {
                currentLines.Clear();
                return;
            }

            var articleSlug = LegalStructureDetector.CreateArticleSlug(currentArticle);
            articleOccurrenceCounts.TryGetValue(articleSlug, out var occurrenceCount);
            articleOccurrenceCounts[articleSlug] = occurrenceCount + 1;

            var suffix = occurrenceCount == 0 ? articleSlug : $"{articleSlug}-{occurrenceCount + 1}";
            var chunkIndex = chunks.Count;
            var text = string.Join(Environment.NewLine, currentLines);

            chunks.Add(new RegulationChunk
            {
                Id = $"{document.Id}-{suffix}",
                DocumentId = document.Id,
                Title = currentLegalTitle,
                Chapter = currentChapter,
                Section = currentSection,
                Article = currentArticle,
                ChunkIndex = chunkIndex,
                Text = text,
                Metadata = CreateMetadata(document, currentLegalTitle, currentChapter, currentSection, currentArticle, chunkIndex)
            });

            currentLines.Clear();
            currentArticle = null;
        }
    }

    private static IReadOnlyDictionary<string, string> CreateMetadata(
        RegulationDocument document,
        string? legalTitle,
        string? chapter,
        string? section,
        string? article,
        int chunkIndex)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["documentId"] = document.Id,
            ["documentTitle"] = document.Title,
            ["source"] = document.Source,
            ["documentType"] = document.DocumentType,
            ["url"] = document.Url,
            ["status"] = document.Status,
            ["requiresReview"] = document.RequiresReview.ToString(),
            ["chunkIndex"] = chunkIndex.ToString()
        };

        AddIfPresent(metadata, "resolutionNumber", document.ResolutionNumber);
        AddIfPresent(metadata, "publicationDate", document.PublicationDate?.ToString("yyyy-MM-dd"));
        AddIfPresent(metadata, "effectiveDate", document.EffectiveDate?.ToString("yyyy-MM-dd"));
        AddIfPresent(metadata, "title", legalTitle);
        AddIfPresent(metadata, "chapter", chapter);
        AddIfPresent(metadata, "section", section);
        AddIfPresent(metadata, "article", article);

        return metadata;
    }

    private static IEnumerable<string> ReadLines(string text) =>
        text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    private static void AddIfPresent(IDictionary<string, string> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value;
        }
    }
}
