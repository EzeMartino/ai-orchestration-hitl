using Orchestration.Application.Agents.Shared;

namespace Orchestration.Application.Agents.Legal;

public interface ILegalAgent
{
    Task<LegalAgentResult> ReviewAsync(
        FinancialReportContext report,
        CancellationToken cancellationToken
    );
}