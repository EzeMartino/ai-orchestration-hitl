namespace Orchestration.Application.Agents.Legal.AiReview;

public static class LegalAnalysisAiReviewSafetyConstraints
{
    public const string ConceptualGoal = "The LegalAgent reviews financial analysis for possible regulatory review areas.";
    public const string NoLegalAdvice = "It must not provide legal advice.";
    public const string NoViolationDetermination = "It must not declare legal violations.";
    public const string DoNotInventRegulations = "It must not invent regulations or citations.";
    public const string UseOnlyProvidedEvidence = "It must use only provided CNV/Infoleg evidence.";
    public const string PreserveUncertainty = "It must preserve uncertainty and require human/legal review.";
}
