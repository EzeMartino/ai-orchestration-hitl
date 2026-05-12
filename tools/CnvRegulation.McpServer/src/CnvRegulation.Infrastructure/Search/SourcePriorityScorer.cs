using CnvRegulation.Domain;

namespace CnvRegulation.Infrastructure.Search;

/// <summary>
/// Scores source authority for ranking regulatory search results.
/// </summary>
public static class SourcePriorityScorer
{
    /// <summary>
    /// Calculates a priority score for one document/chunk pair.
    /// </summary>
    /// <param name="document">The source document.</param>
    /// <param name="chunk">The chunk when available.</param>
    /// <returns>The source priority score.</returns>
    public static double Score(RegulationDocument document, RegulationChunk? chunk = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (IsNonSearchable(document))
        {
            return -0.50;
        }

        var sourceFile = document.Metadata.TryGetValue("sourceFile", out var value) ? value : string.Empty;
        var url = document.Url;
        var text = string.Join(' ', document.Id, document.Title, sourceFile, url, chunk?.Id);

        if (document.Source.Equals("CNV", StringComparison.OrdinalIgnoreCase)
            && (text.Contains("toc2013", StringComparison.OrdinalIgnoreCase)
                || text.Contains("TOC2013", StringComparison.OrdinalIgnoreCase)
                || text.Contains("N.T. 2013", StringComparison.OrdinalIgnoreCase)))
        {
            return 0.25;
        }

        if (document.Source.Equals("CNV", StringComparison.OrdinalIgnoreCase))
        {
            return 0.20;
        }

        if (url.Contains("texact", StringComparison.OrdinalIgnoreCase)
            || sourceFile.Contains("texact", StringComparison.OrdinalIgnoreCase))
        {
            return 0.15;
        }

        if (url.Contains("norma.htm", StringComparison.OrdinalIgnoreCase)
            || sourceFile.Contains("norma", StringComparison.OrdinalIgnoreCase))
        {
            return 0.10;
        }

        if (url.Contains("/anexos/", StringComparison.OrdinalIgnoreCase)
            || url.Contains("verNorma.do", StringComparison.OrdinalIgnoreCase)
            || sourceFile.Contains("anexos", StringComparison.OrdinalIgnoreCase)
            || sourceFile.Contains("vernorma", StringComparison.OrdinalIgnoreCase))
        {
            return 0.05;
        }

        return document.Status.Equals("candidate", StringComparison.OrdinalIgnoreCase) ? -0.10 : 0;
    }

    /// <summary>
    /// Determines whether a document should be excluded by default.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>True when not searchable.</returns>
    public static bool IsNonSearchable(RegulationDocument document) =>
        document.Metadata.TryGetValue("searchable", out var searchable)
        && searchable.Equals("false", StringComparison.OrdinalIgnoreCase);
}
