using System.Collections.Generic;

namespace Orchestration.Application.Agents.Legal.AiReview;

public static class LegalAnalysisAiReviewLanguageRules
{
    public static readonly IReadOnlyList<string> AllowedLanguage = new[]
    {
        "possible regulatory review area",
        "may require review",
        "should be reviewed by a qualified professional",
        "available CNV/Infoleg evidence suggests a review area"
    };

    public static readonly IReadOnlyList<string> ForbiddenLanguage = new[]
    {
        "this is illegal",
        "violates regulation",
        "breached CNV rules",
        "legal violation confirmed",
        "non-compliance confirmed",
        "guilty",
        "fraud"
    };
}
