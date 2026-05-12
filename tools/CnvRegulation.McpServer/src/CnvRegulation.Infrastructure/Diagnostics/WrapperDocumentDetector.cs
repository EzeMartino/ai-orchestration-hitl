using System.Text.RegularExpressions;
using CnvRegulation.Domain;

namespace CnvRegulation.Infrastructure.Diagnostics;

/// <summary>
/// Detects low-value wrapper-like HTML documents.
/// </summary>
public sealed partial class WrapperDocumentDetector
{
    /// <summary>
    /// Determines whether a document should be marked as a wrapper candidate.
    /// </summary>
    /// <param name="document">The parsed document.</param>
    /// <param name="chunkCount">The detected article chunk count.</param>
    /// <returns>Wrapper detection result.</returns>
    public WrapperDetectionResult Detect(RegulationDocument document, int chunkCount)
    {
        ArgumentNullException.ThrowIfNull(document);

        var linkCount = CountLinks(document.Text);
        var fileType = document.Metadata.TryGetValue("fileType", out var value) ? value : string.Empty;
        var genericInfolegTitle = document.Title.Contains("InfoLEG", StringComparison.OrdinalIgnoreCase)
            || document.Title.Equals("Discovered Infoleg source", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(fileType, "HTML", StringComparison.OrdinalIgnoreCase)
            && string.Equals(document.Source, "Infoleg", StringComparison.OrdinalIgnoreCase)
            && (chunkCount == 0
                || (document.Text.Length < 2_000 && linkCount > 20)
                || (genericInfolegTitle && chunkCount == 0)))
        {
            return new WrapperDetectionResult(
                IsWrapperCandidate: true,
                Searchable: false,
                Reason: "too many links and too little article text");
        }

        return new WrapperDetectionResult(
            IsWrapperCandidate: false,
            Searchable: true,
            Reason: string.Empty);
    }

    private static int CountLinks(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return LinkRegex().Matches(text).Count;
    }

    [GeneratedRegex(@"https?://|www\.|href\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LinkRegex();
}

/// <summary>
/// Wrapper detection result.
/// </summary>
/// <param name="IsWrapperCandidate">Whether the document is likely a wrapper.</param>
/// <param name="Searchable">Whether it should be searchable by default.</param>
/// <param name="Reason">The detection reason.</param>
public sealed record WrapperDetectionResult(
    bool IsWrapperCandidate,
    bool Searchable,
    string Reason);
