using CnvRegulation.Application.Abstractions;
using CnvRegulation.Application.Contracts;
using CnvRegulation.Domain;

namespace CnvRegulation.Application.Analysis;

/// <summary>
/// Builds conservative, evidence-based findings from cited CNV search results.
/// </summary>
public sealed class RegulatoryFindingBuilder : IRegulatoryFindingBuilder
{
    /// <inheritdoc />
    public IReadOnlyList<ComplianceFinding> BuildFindings(
        string inputText,
        IReadOnlyList<RegulatoryTopic> topics,
        IReadOnlyList<RegulationSearchResult> searchResults,
        bool strictMode)
    {
        ArgumentNullException.ThrowIfNull(topics);
        ArgumentNullException.ThrowIfNull(searchResults);

        var citedResults = searchResults
            .Where(result => result.Citations.Count > 0)
            .GroupBy(result => result.ChunkId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(result => result.Score).First())
            .OrderByDescending(result => result.Score)
            .ToArray();

        if (topics.Count == 0 || citedResults.Length == 0)
        {
            return [];
        }

        var findings = new List<ComplianceFinding>();
        foreach (var topic in topics.Take(3))
        {
            var result = citedResults
                .FirstOrDefault(candidate => MatchesTopic(candidate, topic))
                ?? citedResults[Math.Min(findings.Count, citedResults.Length - 1)];
            var citation = result.Citations[0];

            findings.Add(new ComplianceFinding
            {
                RiskLevel = RiskLevelClassifier.Classify(inputText, topic.Name),
                Issue = CreateIssue(topic),
                Citation = CopyCitation(citation, result.Snippet),
                ReasoningSummary = CreateReasoning(topic),
                Confidence = CalculateConfidence(topic, result, strictMode)
            });
        }

        return findings
            .GroupBy(finding => string.Join('|', finding.Issue, finding.Citation.Url, finding.Citation.Article), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static bool MatchesTopic(RegulationSearchResult result, RegulatoryTopic topic)
    {
        var evidence = string.Join(' ', result.Title, result.Article, result.Snippet, result.Citations[0].QuotedText);
        return topic.SearchQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length > 3)
            .Any(term => evidence.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string CreateIssue(RegulatoryTopic topic) =>
        topic.Name switch
        {
            "Client information and suitability" =>
                "The text may require review against CNV rules related to client information, suitability or agent obligations.",
            "Prevencion de lavado" =>
                "The text may require review against CNV rules related to prevention of money laundering or terrorist financing.",
            "Oferta publica" =>
                "The text may require review against CNV rules related to public offering or negotiable securities.",
            "Hecho relevante" =>
                "The text may require review against CNV rules related to relevant information or disclosure channels.",
            _ =>
                $"The text may require review against CNV rules related to {topic.Name}."
        };

    private static string CreateReasoning(RegulatoryTopic topic) =>
        $"Relevant CNV material was found concerning {topic.Name}. Human legal review is recommended.";

    private static double CalculateConfidence(RegulatoryTopic topic, RegulationSearchResult result, bool strictMode)
    {
        var scoreComponent = Math.Min(0.18, Math.Max(0, result.Score) * 0.08);
        var strictAdjustment = strictMode ? 0.02 : -0.03;
        return Math.Round(Math.Clamp(0.48 + (topic.Confidence * 0.18) + scoreComponent + strictAdjustment, 0.35, 0.82), 2);
    }

    private static RegulationCitation CopyCitation(RegulationCitation citation, string snippet) =>
        new()
        {
            Source = citation.Source,
            DocumentType = citation.DocumentType,
            ResolutionNumber = citation.ResolutionNumber,
            Title = citation.Title,
            Chapter = citation.Chapter,
            Section = citation.Section,
            Article = citation.Article,
            PublicationDate = citation.PublicationDate,
            Url = citation.Url,
            QuotedText = string.IsNullOrWhiteSpace(citation.QuotedText) ? snippet : citation.QuotedText
        };
}
