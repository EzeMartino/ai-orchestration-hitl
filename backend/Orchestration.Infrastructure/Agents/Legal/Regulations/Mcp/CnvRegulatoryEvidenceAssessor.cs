using Orchestration.Application.Agents.Legal.Regulations;

namespace Orchestration.Infrastructure.Agents.Legal.Regulations.Mcp;

internal static class CnvRegulatoryEvidenceAssessor
{
    internal const double WeakRelevanceThreshold = 0.40;
    internal const double StrongRelevanceThreshold = 0.75;

    internal static bool IsRelevant(CnvRegulationSearchResult result) =>
        ClassifyRelevance(result.Score) is "Weak" or "Strong";

    internal static RegulatoryEvidenceAssessment Assess(
        IReadOnlyList<CnvRegulationSearchResult> results)
    {
        if (results.Count == 0)
        {
            return new RegulatoryEvidenceAssessment(
                EvidenceFound: false,
                Relevance: "None",
                Applicability: "NotEstablished",
                EvidenceQuality: "None",
                Severity: "Info",
                RequiresHumanReview: false,
                Reasons:
                [
                    "La búsqueda MCP no devolvió evidencia regulatoria y no establece aplicabilidad legal."
                ]);
        }

        var relevance = MaxLevel(results.Select(result => ClassifyRelevance(result.Score)));
        var quality = MaxLevel(results.Select(ClassifyEvidenceQuality));
        var requiresHumanReview = relevance is "Weak" or "Strong";
        var reasons = new List<string>
        {
            relevance switch
            {
                "Strong" => "La puntuación de relevancia de recuperación alcanzó al menos 0,75.",
                "Weak" => "La puntuación de relevancia de recuperación fue al menos 0,40 y menor que 0,75.",
                _ => "La puntuación de relevancia de recuperación fue inferior a 0,40."
            },
            quality switch
            {
                "Strong" => "Existe una cita con fuente, título, localizador, URL y texto citado.",
                "Weak" => "Existe una cita, pero sus datos de trazabilidad están incompletos.",
                _ => "No se recuperaron citas normativas."
            },
            "La recuperación no establece aplicabilidad legal."
        };

        return new RegulatoryEvidenceAssessment(
            EvidenceFound: true,
            Relevance: relevance,
            Applicability: "NotEstablished",
            EvidenceQuality: quality,
            Severity: requiresHumanReview ? "Warning" : "Info",
            RequiresHumanReview: requiresHumanReview,
            Reasons: reasons);
    }

    private static string ClassifyRelevance(double score)
    {
        if (!double.IsFinite(score) || score < WeakRelevanceThreshold)
        {
            return "None";
        }

        return score < StrongRelevanceThreshold ? "Weak" : "Strong";
    }

    private static string ClassifyEvidenceQuality(CnvRegulationSearchResult result)
    {
        if (result.Citations is not { Count: > 0 })
        {
            return "None";
        }

        return result.Citations.Any(IsStrongCitation) ? "Strong" : "Weak";
    }

    private static bool IsStrongCitation(CnvRegulationCitation citation)
    {
        var hasLocator =
            !string.IsNullOrWhiteSpace(citation.Article) ||
            !string.IsNullOrWhiteSpace(citation.Section) ||
            !string.IsNullOrWhiteSpace(citation.Chapter);

        return
            !string.IsNullOrWhiteSpace(citation.Source) &&
            !string.IsNullOrWhiteSpace(citation.Title) &&
            hasLocator &&
            !string.IsNullOrWhiteSpace(citation.Url) &&
            !string.IsNullOrWhiteSpace(citation.QuotedText);
    }

    private static string MaxLevel(IEnumerable<string> levels)
    {
        var values = levels.ToArray();
        if (values.Contains("Strong")) return "Strong";
        if (values.Contains("Weak")) return "Weak";
        return "None";
    }
}
