using Orchestration.Application.Agents.Legal.Regulations;

namespace Orchestration.Application.Agents.Legal.AiReview;

public static class RegulatoryEvidenceIdentity
{
    public static bool MatchesOriginal(
        LegalEvidenceReference reference,
        RegulatoryOriginalEvidence original)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(original);

        var citation = original.Citation;
        var locator = GetCitationLocator(citation);
        if (!EqualNonBlank(reference.Citation, locator, StringComparison.Ordinal) ||
            !SourceMatches(reference.Source, citation) ||
            Conflicts(reference.Title, citation.Title) ||
            Conflicts(reference.Url, citation.Url) ||
            SnippetConflicts(reference.Snippet, original))
        {
            return false;
        }

        return EqualNonBlank(reference.Title, citation.Title, StringComparison.Ordinal) ||
               EqualNonBlank(reference.Url, citation.Url, StringComparison.Ordinal) ||
               HasStrongCompositeSourceMatch(reference.Source, citation);
    }

    public static bool RepresentsSameDocument(
        LegalEvidenceReference left,
        LegalEvidenceReference right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (!EqualNonBlank(left.Citation, right.Citation, StringComparison.OrdinalIgnoreCase) ||
            Conflicts(left.Source, right.Source) ||
            Conflicts(left.Title, right.Title) ||
            Conflicts(left.Url, right.Url))
        {
            return false;
        }

        return EqualNonBlank(left.Title, right.Title, StringComparison.Ordinal) ||
               EqualNonBlank(left.Url, right.Url, StringComparison.Ordinal);
    }

    public static bool MatchesAllowedReference(
        LegalEvidenceReference proposed,
        LegalEvidenceReference allowed)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentNullException.ThrowIfNull(allowed);

        return EqualNonBlank(
                   proposed.Citation,
                   allowed.Citation,
                   StringComparison.OrdinalIgnoreCase) &&
               EqualNonBlank(proposed.Source, allowed.Source, StringComparison.Ordinal) &&
               EqualNonBlank(proposed.Title, allowed.Title, StringComparison.Ordinal) &&
               EqualOptional(proposed.Url, allowed.Url, StringComparison.Ordinal);
    }

    public static bool RepresentsSameCitationDocument(
        RegulatoryEvidenceCitation candidate,
        RegulatoryEvidenceCitation original)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(original);

        if (!EqualNonBlank(
                GetCitationLocator(candidate),
                GetCitationLocator(original),
                StringComparison.Ordinal) ||
            Conflicts(candidate.Source, original.Source) ||
            Conflicts(candidate.DocumentType, original.DocumentType) ||
            Conflicts(candidate.ResolutionNumber, original.ResolutionNumber) ||
            Conflicts(candidate.Title, original.Title) ||
            Conflicts(candidate.Url, original.Url))
        {
            return false;
        }

        return EqualNonBlank(
                   candidate.ResolutionNumber,
                   original.ResolutionNumber,
                   StringComparison.Ordinal) ||
               EqualNonBlank(candidate.Title, original.Title, StringComparison.Ordinal) ||
               EqualNonBlank(candidate.Url, original.Url, StringComparison.Ordinal);
    }

    public static string? GetCitationLocator(RegulatoryEvidenceCitation citation)
    {
        ArgumentNullException.ThrowIfNull(citation);
        return citation.Article ?? citation.Section ?? citation.Chapter;
    }

    private static bool SourceMatches(
        string? referenceSource,
        RegulatoryEvidenceCitation citation)
    {
        if (string.IsNullOrWhiteSpace(referenceSource) ||
            string.IsNullOrWhiteSpace(citation.Source))
        {
            return true;
        }

        return EqualNonBlank(
                   referenceSource,
                   citation.Source,
                   StringComparison.Ordinal) ||
               EqualNonBlank(
                   referenceSource,
                   BuildCompositeSource(citation),
                   StringComparison.Ordinal);
    }

    private static bool HasStrongCompositeSourceMatch(
        string? referenceSource,
        RegulatoryEvidenceCitation citation)
    {
        return (!string.IsNullOrWhiteSpace(citation.ResolutionNumber) ||
                !string.IsNullOrWhiteSpace(citation.Url)) &&
               EqualNonBlank(
                   referenceSource,
                   BuildCompositeSource(citation),
                   StringComparison.Ordinal);
    }

    private static string BuildCompositeSource(RegulatoryEvidenceCitation citation)
    {
        return string.Join(
            " | ",
            new[]
            {
                citation.Source,
                citation.DocumentType,
                citation.ResolutionNumber,
                citation.Url
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static bool SnippetConflicts(
        string? referenceSnippet,
        RegulatoryOriginalEvidence original)
    {
        if (string.IsNullOrWhiteSpace(referenceSnippet))
        {
            return false;
        }

        var candidates = new[]
            {
                original.Snippet,
                original.Citation.QuotedText
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        return candidates.Length > 0 &&
               !candidates.Any(candidate => EqualNonBlank(
                   referenceSnippet,
                   candidate,
                   StringComparison.Ordinal));
    }

    private static bool Conflicts(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               !string.IsNullOrWhiteSpace(right) &&
               !string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);
    }

    private static bool EqualOptional(
        string? left,
        string? right,
        StringComparison comparison)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right);
        }

        return string.Equals(left.Trim(), right.Trim(), comparison);
    }

    private static bool EqualNonBlank(
        string? left,
        string? right,
        StringComparison comparison)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               !string.IsNullOrWhiteSpace(right) &&
               string.Equals(left.Trim(), right.Trim(), comparison);
    }
}
