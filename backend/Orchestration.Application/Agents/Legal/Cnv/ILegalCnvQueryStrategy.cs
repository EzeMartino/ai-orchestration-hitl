using System.Collections.Generic;
using Orchestration.Application.Agents.Data.FinancialAnalysis;

namespace Orchestration.Application.Agents.Legal.Cnv;

public interface ILegalCnvQueryStrategy
{
    IReadOnlyList<LegalCnvQuery> BuildQueries(
        FinancialAnalysisContext? financialAnalysis);
}
