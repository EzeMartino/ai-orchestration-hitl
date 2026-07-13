using Orchestration.Domain.AnalysisSessions;

namespace Orchestration.Application.Agents.Shared;

public interface IFinancialReportContextResolver
{
    FinancialReportContextResolution Resolve(AnalysisSession session);
}
