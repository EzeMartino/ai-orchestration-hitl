using Orchestration.Application.Agents.Shared;

namespace Orchestration.Application.Agents.Data;

public interface IDataAgent
{
    Task<DataAgentResult> AnalyzeAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken
    );
}