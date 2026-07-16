using System.Collections.Generic;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Application.Agents.Legal.Cnv;

public interface ILegalCnvQueryStrategy
{
    LegalCnvQueryPlan BuildPlan(
        FinancialAnalysisContext? financialAnalysis,
        LegalDataEvidenceContext dataEvidence);

    IReadOnlyList<LegalCnvQuery> BuildQueries(
        FinancialAnalysisContext? financialAnalysis)
    {
        return BuildPlan(
            financialAnalysis,
            LegalDataEvidenceClassifier.Classify(
                LegalDataToolStatuses.Executed,
                financialAnalysis)
        ).Queries;
    }
}
