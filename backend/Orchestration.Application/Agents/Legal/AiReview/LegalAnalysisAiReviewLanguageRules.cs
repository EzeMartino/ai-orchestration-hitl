using System.Collections.Generic;

namespace Orchestration.Application.Agents.Legal.AiReview;

public static class LegalAnalysisAiReviewLanguageRules
{
    public static readonly IReadOnlyList<string> AllowedLanguage = new[]
    {
        "possible regulatory review area",
        "may require review",
        "should be reviewed by a qualified professional",
        "available CNV/Infoleg evidence suggests a review area",
        "No constituye asesoramiento legal"
    };

    public static readonly IReadOnlyList<string> ForbiddenLanguage = new[]
    {
        "this is illegal",
        "violates regulation",
        "breached CNV rules",
        "legal violation confirmed",
        "non-compliance confirmed",
        "guilty",
        "fraud",
        "es ilegal",
        "infringe la normativa",
        "incumplimiento confirmado",
        "violación legal confirmada",
        "culpable",
        "cometió fraude"
    };

    public static readonly IReadOnlyList<string> ConclusiveLanguage = new[]
    {
        "this is illegal",
        "violates regulation",
        "breached CNV rules",
        "legal violation confirmed",
        "non-compliance confirmed",
        "guilty",
        "es ilegal",
        "infringe la normativa",
        "infringió la normativa",
        "incumple la normativa",
        "incumplió la normativa",
        "incumplimiento confirmado",
        "violación legal confirmada",
        "culpable",
        "cometió fraude",
        "fraude confirmado"
    };
}
