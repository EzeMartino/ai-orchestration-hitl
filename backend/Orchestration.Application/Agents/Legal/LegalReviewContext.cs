using Orchestration.Application.Agents.Legal.Cnv;

namespace Orchestration.Application.Agents.Legal;

public enum FinancialAnalysisResolutionMode
{
    ProvidedOrPersisted,
    ProvidedOnly
}

public sealed record LegalReviewContext(
    FinancialAnalysisResolutionMode ResolutionMode,
    LegalDataEvidenceContext? DataEvidence = null)
{
    public static LegalReviewContext Default { get; } = new(
        FinancialAnalysisResolutionMode.ProvidedOrPersisted
    );
}
