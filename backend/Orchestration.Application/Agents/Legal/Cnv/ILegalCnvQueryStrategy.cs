using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Application.Agents.Legal.Cnv;

public interface ILegalCnvQueryStrategy
{
    LegalCnvQueryPlan BuildPlan(
        FinancialAnalysisContext? financialAnalysis,
        LegalDataEvidenceContext dataEvidence);
}
